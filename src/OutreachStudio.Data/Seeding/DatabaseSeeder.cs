using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using OutreachStudio.Data.Synthetic;
using OutreachStudio.Engine.Audience;
using OutreachStudio.Engine.Campaigns;
using OutreachStudio.Engine.Messaging;
using OutreachStudio.Engine.Rules;
using OutreachStudio.Engine.Scheduling;

namespace OutreachStudio.Data.Seeding;

public static class Reviewers
{
    public static readonly IReadOnlyList<string> All = ["Lena (campaign lead)", "Tomasz (compliance)"];
}

/// <summary>
/// Fills an empty database with the synthetic user base and the campaign history. Runs once, on
/// the first start of the web app. Users, events, deliveries and attempts go in through binary
/// COPY because EF Core's change tracker is the wrong tool for half a million rows.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedIfEmptyAsync(OutreachDbContext db, DateTimeOffset now, ILogger logger, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var users = SyntheticUsers.Generate(now);
        var rng = new Random(SyntheticUsers.DefaultSeed + 1);
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        await CopyUsers(conn, users, ct);
        var eventCount = await CopyEvents(conn, users, ct);
        logger.LogInformation("Seeded {Users} users and {Events} events", users.Count, eventCount);

        var suppressed = users.Where(_ => rng.NextDouble() < 0.002).Select(u => u.Email).ToHashSet(StringComparer.Ordinal);
        db.Suppressions.AddRange(suppressed.Select(e => new Suppression { Email = e, Reason = "Unsubscribed via support", CreatedAt = now.AddDays(-rng.Next(1, 200)) }));

        var deliveries = new List<Delivery>();
        var attempts = new List<DeliveryAttempt>();
        foreach (var seed in SeedCampaigns.All)
        {
            var campaign = BuildCampaign(seed, now, rng);
            db.Campaigns.Add(campaign);
            if (seed.Status is CampaignStatus.Done or CampaignStatus.Failed or CampaignStatus.Scheduled)
            {
                var (rows, tries) = BuildDeliveries(campaign, seed, users, suppressed, now, rng, deliveries.Count, attempts.Count);
                campaign.AudienceSize = rows.Select(r => r.UserId).Distinct().Count();
                campaign.FinishedAt = seed.Status is CampaignStatus.Scheduled ? null : rows.Where(r => r.SentAt is not null).Select(r => r.SentAt).DefaultIfEmpty(campaign.ScheduledAt).Max();
                deliveries.AddRange(rows);
                attempts.AddRange(tries);
            }
        }
        await db.SaveChangesAsync(ct);

        await CopyDeliveries(conn, deliveries, ct);
        await CopyAttempts(conn, attempts, ct);
        await ResetSequences(conn, ct);
        logger.LogInformation("Seeded {Campaigns} campaigns, {Deliveries} deliveries, {Attempts} attempts", SeedCampaigns.All.Count, deliveries.Count, attempts.Count);
    }

    private static Campaign BuildCampaign(SeedCampaign seed, DateTimeOffset now, Random rng)
    {
        var start = seed.Status is CampaignStatus.Scheduled
            ? now.AddSeconds(90)
            : new DateTimeOffset(now.UtcDateTime.Date.AddDays(-seed.DaysAgo).AddHours(10 + rng.Next(0, 5)), TimeSpan.Zero);
        var created = start.AddDays(-2).AddHours(-rng.Next(1, 30));
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            Name = seed.Name,
            Status = seed.Status,
            CreatedAt = created,
            UpdatedAt = seed.Status is CampaignStatus.Draft ? created.AddHours(3) : start,
            CurrentVersion = 1,
            ScheduledAt = seed.Status is CampaignStatus.Draft or CampaignStatus.InReview or CampaignStatus.Approved ? null : start,
        };
        campaign.Versions.Add(new CampaignVersion
        {
            CampaignId = campaign.Id,
            Number = 1,
            CreatedAt = created,
            RuleJson = seed.Rule.ToJson(),
            Channels = seed.Channels,
            PushTitle = seed.PushTitle,
            PushBody = seed.PushBody,
            EmailSubject = seed.EmailSubject,
            EmailBody = seed.EmailBody,
            StartAt = start,
            PerMinute = seed.PerMinute,
            HoldoutShare = seed.HoldoutShare,
        });
        for (var i = 0; i < seed.Approvals; i++)
        {
            campaign.Approvals.Add(new Approval { CampaignId = campaign.Id, Version = 1, Reviewer = Reviewers.All[i], At = created.AddHours(20 + i * 5) });
        }
        return campaign;
    }

    private static (List<Delivery> Rows, List<DeliveryAttempt> Attempts) BuildDeliveries(
        Campaign campaign, SeedCampaign seed, IReadOnlyList<AudienceUser> users, HashSet<string> suppressed,
        DateTimeOffset now, Random rng, int deliveryIdOffset, int attemptIdOffset)
    {
        var version = campaign.Versions[0];
        var evaluatedAt = seed.Status is CampaignStatus.Scheduled ? now : version.StartAt;
        var audience = AudienceEstimate.Select(seed.Rule, users, evaluatedAt);
        var plan = RolloutPlanner.Plan(campaign.Id, audience, version.Schedule);
        var byId = users.ToDictionary(u => u.Id);
        var rows = new List<Delivery>();
        var attempts = new List<DeliveryAttempt>();
        var channels = new[] { Channel.Push, Channel.Email }.Where(c => version.Channels.HasFlag(c == Channel.Push ? Channels.Push : Channels.Email));

        foreach (var planned in plan)
        {
            var user = byId[planned.UserId];
            foreach (var channel in channels)
            {
                var row = new Delivery
                {
                    Id = deliveryIdOffset + rows.Count + 1,
                    CampaignId = campaign.Id,
                    UserId = user.Id,
                    Channel = channel,
                    DueAt = planned.DueAt,
                    Status = DeliveryStatus.Queued,
                };
                rows.Add(row);
                if (seed.Status is CampaignStatus.Scheduled)
                {
                    row.Status = planned.Holdout ? DeliveryStatus.Holdout : DeliveryStatus.Queued;
                    continue;
                }
                Resolve(row, user, planned.Holdout, channel, seed.Outcome, suppressed, rng, attempts, attemptIdOffset);
            }
        }
        return (rows, attempts);
    }

    private static void Resolve(Delivery row, AudienceUser user, bool holdout, Channel channel, Outcome outcome,
        HashSet<string> suppressed, Random rng, List<DeliveryAttempt> attempts, int attemptIdOffset)
    {
        if (holdout)
        {
            row.Status = DeliveryStatus.Holdout;
            return;
        }
        if (!user.MarketingConsent)
        {
            row.Status = DeliveryStatus.Skipped;
            row.SkipReason = SkipReason.ConsentRevoked;
            return;
        }
        if (suppressed.Contains(user.Email))
        {
            row.Status = DeliveryStatus.Skipped;
            row.SkipReason = SkipReason.Suppressed;
            return;
        }
        if (rng.NextDouble() < 0.03)
        {
            row.Status = DeliveryStatus.Skipped;
            row.SkipReason = SkipReason.FrequencyCap;
            return;
        }

        var primary = channel is Channel.Push ? "push-a" : "email-a";
        var secondary = channel is Channel.Push ? "push-b" : "email-b";
        var failShare = (outcome, channel) switch
        {
            (Outcome.EmailOutage, Channel.Email) => 0.35,
            (Outcome.PushCollapse, Channel.Push) => 0.92,
            _ => 0.015,
        };
        var failed = rng.NextDouble() < failShare;
        var failedOver = !failed && rng.NextDouble() < (outcome is Outcome.Healthy ? 0.04 : 0.3);
        row.Provider = failedOver ? secondary : primary;
        row.Attempts = failed ? 3 : failedOver ? 2 : 1;

        var at = row.DueAt.AddSeconds(rng.NextDouble() * 3);
        for (var i = 0; i < row.Attempts; i++)
        {
            var last = i == row.Attempts - 1;
            var ok = !failed && last;
            attempts.Add(new DeliveryAttempt
            {
                Id = attemptIdOffset + attempts.Count + 1,
                DeliveryId = row.Id,
                Provider = last ? row.Provider! : primary,
                StartedAt = at.AddSeconds(i * (2 + rng.NextDouble() * 6)),
                LatencyMs = ok ? rng.Next(40, 400) : rng.Next(800, 3000),
                Succeeded = ok,
                Error = ok ? null : $"{(last ? row.Provider : primary)}: 503 Service unavailable",
            });
        }

        if (failed)
        {
            row.Status = DeliveryStatus.Failed;
            row.LastError = $"{row.Provider}: 503 Service unavailable after {row.Attempts} attempts";
            return;
        }

        row.SentAt = at.AddSeconds(row.Attempts * 3);
        row.ProviderMessageId = $"{row.Provider}-{Guid.NewGuid():N}";
        row.Status = DeliveryStatus.Sent;
        if (rng.NextDouble() < 0.96)
        {
            row.DeliveredAt = row.SentAt.Value.AddSeconds(2 + rng.NextDouble() * 60);
            row.Status = DeliveryStatus.Delivered;
            var openShare = channel is Channel.Push ? 0.38 : 0.27;
            if (rng.NextDouble() < openShare)
            {
                row.OpenedAt = row.DeliveredAt.Value.AddMinutes(1 + rng.NextDouble() * 360);
                row.Status = DeliveryStatus.Opened;
            }
        }
    }

    private static async Task CopyUsers(NpgsqlConnection conn, IReadOnlyList<AudienceUser> users, CancellationToken ct)
    {
        await using var writer = await conn.BeginBinaryImportAsync(
            "COPY users (id, first_name, email, country, tier, signup_date, last_active, platform, marketing_consent, time_zone, points_balance) FROM STDIN (FORMAT BINARY)", ct);
        foreach (var u in users)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(u.Id, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(u.FirstName, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(u.Email, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(u.Country, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(u.Tier.ToString(), NpgsqlDbType.Text, ct);
            await writer.WriteAsync(u.SignupDate, NpgsqlDbType.Date, ct);
            await writer.WriteAsync(u.LastActive, NpgsqlDbType.Date, ct);
            await writer.WriteAsync(u.Platform.ToString(), NpgsqlDbType.Text, ct);
            await writer.WriteAsync(u.MarketingConsent, NpgsqlDbType.Boolean, ct);
            await writer.WriteAsync(u.TimeZone, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(u.PointsBalance, NpgsqlDbType.Integer, ct);
        }
        await writer.CompleteAsync(ct);
    }

    private static async Task<long> CopyEvents(NpgsqlConnection conn, IReadOnlyList<AudienceUser> users, CancellationToken ct)
    {
        long id = 0;
        await using var writer = await conn.BeginBinaryImportAsync("COPY user_events (id, user_id, type, occurred_at) FROM STDIN (FORMAT BINARY)", ct);
        foreach (var u in users)
        {
            foreach (var (type, stamps) in u.Events)
            {
                foreach (var stamp in stamps)
                {
                    await writer.StartRowAsync(ct);
                    await writer.WriteAsync(++id, NpgsqlDbType.Bigint, ct);
                    await writer.WriteAsync(u.Id, NpgsqlDbType.Integer, ct);
                    await writer.WriteAsync(type.ToString(), NpgsqlDbType.Text, ct);
                    await writer.WriteAsync(DateTimeOffset.FromUnixTimeSeconds(stamp), NpgsqlDbType.TimestampTz, ct);
                }
            }
        }
        await writer.CompleteAsync(ct);
        return id;
    }

    private static async Task CopyDeliveries(NpgsqlConnection conn, List<Delivery> rows, CancellationToken ct)
    {
        await using var writer = await conn.BeginBinaryImportAsync(
            "COPY deliveries (id, campaign_id, user_id, channel, status, skip_reason, due_at, attempts, provider, last_error, claimed_by, claimed_at, sent_at, delivered_at, opened_at, provider_message_id) FROM STDIN (FORMAT BINARY)", ct);
        foreach (var d in rows)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(d.Id, NpgsqlDbType.Bigint, ct);
            await writer.WriteAsync(d.CampaignId, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(d.UserId, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(d.Channel.ToString(), NpgsqlDbType.Text, ct);
            await writer.WriteAsync(d.Status.ToString(), NpgsqlDbType.Text, ct);
            await writer.WriteAsync(d.SkipReason.ToString(), NpgsqlDbType.Text, ct);
            await writer.WriteAsync(d.DueAt, NpgsqlDbType.TimestampTz, ct);
            await writer.WriteAsync(d.Attempts, NpgsqlDbType.Integer, ct);
            await WriteNullable(writer, d.Provider, NpgsqlDbType.Text, ct);
            await WriteNullable(writer, d.LastError, NpgsqlDbType.Text, ct);
            await WriteNullable(writer, d.ClaimedBy, NpgsqlDbType.Text, ct);
            await WriteNullable(writer, d.ClaimedAt, NpgsqlDbType.TimestampTz, ct);
            await WriteNullable(writer, d.SentAt, NpgsqlDbType.TimestampTz, ct);
            await WriteNullable(writer, d.DeliveredAt, NpgsqlDbType.TimestampTz, ct);
            await WriteNullable(writer, d.OpenedAt, NpgsqlDbType.TimestampTz, ct);
            await WriteNullable(writer, d.ProviderMessageId, NpgsqlDbType.Text, ct);
        }
        await writer.CompleteAsync(ct);
    }

    private static async Task CopyAttempts(NpgsqlConnection conn, List<DeliveryAttempt> rows, CancellationToken ct)
    {
        await using var writer = await conn.BeginBinaryImportAsync(
            "COPY delivery_attempts (id, delivery_id, provider, started_at, latency_ms, succeeded, error) FROM STDIN (FORMAT BINARY)", ct);
        foreach (var a in rows)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(a.Id, NpgsqlDbType.Bigint, ct);
            await writer.WriteAsync(a.DeliveryId, NpgsqlDbType.Bigint, ct);
            await writer.WriteAsync(a.Provider, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(a.StartedAt, NpgsqlDbType.TimestampTz, ct);
            await writer.WriteAsync(a.LatencyMs, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(a.Succeeded, NpgsqlDbType.Boolean, ct);
            await WriteNullable(writer, a.Error, NpgsqlDbType.Text, ct);
        }
        await writer.CompleteAsync(ct);
    }

    private static async Task WriteNullable<T>(NpgsqlBinaryImporter writer, T? value, NpgsqlDbType type, CancellationToken ct)
    {
        if (value is null)
        {
            await writer.WriteNullAsync(ct);
        }
        else
        {
            await writer.WriteAsync(value, type, ct);
        }
    }

    private static async Task ResetSequences(NpgsqlConnection conn, CancellationToken ct)
    {
        foreach (var table in new[] { "users", "user_events", "deliveries", "delivery_attempts" })
        {
            await using var cmd = new NpgsqlCommand($"SELECT setval(pg_get_serial_sequence('{table}', 'id'), COALESCE((SELECT MAX(id) FROM {table}), 1))", conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
