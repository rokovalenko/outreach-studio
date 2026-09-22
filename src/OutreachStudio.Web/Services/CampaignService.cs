using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using OutreachStudio.Data;
using OutreachStudio.Engine.Campaigns;
using OutreachStudio.Engine.Messaging;
using OutreachStudio.Engine.Rules;
using OutreachStudio.Engine.Scheduling;
using OutreachStudio.Web.Client.Api;

namespace OutreachStudio.Web.Services;

/// <summary>One line of the dashboard table.</summary>
public sealed record CampaignRow(
    Guid Id,
    string Name,
    CampaignStatus Status,
    int? AudienceSize,
    int Sent,
    int Delivered,
    int Opened,
    DateTimeOffset UpdatedAt);

public sealed record ChannelResult(Channel Channel, int Rows, int Sent, int Delivered, int Opened, int Failed, int Skipped);

public sealed record ProviderResult(string Provider, int Sent, int Failed);

public sealed record DeadLetter(int UserId, Channel Channel, string? Provider, int Attempts, string? LastError);

public sealed record CampaignResults(
    int AudienceSize,
    int Rows,
    IReadOnlyDictionary<DeliveryStatus, int> ByStatus,
    IReadOnlyDictionary<SkipReason, int> Skips,
    IReadOnlyList<ChannelResult> ByChannel,
    IReadOnlyList<ProviderResult> ByProvider,
    int Holdout,
    IReadOnlyList<DeadLetter> DeadLetters)
{
    public int Sent => ByChannel.Sum(c => c.Sent);
    public int Delivered => ByChannel.Sum(c => c.Delivered);
    public int Opened => ByChannel.Sum(c => c.Opened);
}

/// <summary>
/// Everything a campaign does between being typed and being queued. The editor, the campaign page
/// and the JSON endpoints all go through here, so the state machine lives in one place.
/// </summary>
public sealed class CampaignService(OutreachDbContext db, AudienceCache audience, IConfiguration configuration)
{
    /// <summary>Above this share of the user base a campaign needs two approvals instead of one.</summary>
    public double BlastRadiusShare { get; } = configuration.GetValue("Campaigns:BlastRadiusShare", 0.4);

    public async Task<IReadOnlyList<CampaignRow>> ListAsync(CancellationToken ct)
    {
        var campaigns = await db.Campaigns.AsNoTracking().OrderByDescending(c => c.UpdatedAt).ToListAsync(ct);
        var counts = await db.Deliveries.AsNoTracking()
            .GroupBy(d => d.CampaignId)
            .Select(g => new
            {
                CampaignId = g.Key,
                Sent = g.Count(d => d.SentAt != null),
                Delivered = g.Count(d => d.DeliveredAt != null),
                Opened = g.Count(d => d.OpenedAt != null),
            })
            .ToDictionaryAsync(x => x.CampaignId, ct);

        return campaigns.Select(c =>
        {
            counts.TryGetValue(c.Id, out var n);
            return new CampaignRow(c.Id, c.Name, c.Status, c.AudienceSize, n?.Sent ?? 0, n?.Delivered ?? 0, n?.Opened ?? 0, c.UpdatedAt);
        }).ToList();
    }

    /// <summary>
    /// The context lives as long as the Blazor circuit, so what it tracked on the last read is
    /// dropped first. Otherwise a page keeps showing the status a worker has already moved on from.
    /// </summary>
    public async Task<Campaign> LoadAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        return await db.Campaigns.Include(c => c.Versions).Include(c => c.Approvals).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new InvalidOperationException($"No campaign {id}");
    }

    public static CampaignVersion Version(Campaign campaign, int number) =>
        campaign.Versions.First(v => v.Number == number);

    public static CampaignVersion Current(Campaign campaign) => Version(campaign, campaign.CurrentVersion);

    public async Task<Guid> CreateDraftAsync(string name, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            Name = name,
            Status = CampaignStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            CurrentVersion = 1,
        };
        var schedule = Schedule.Default(now.AddHours(1));
        campaign.Versions.Add(new CampaignVersion
        {
            CampaignId = campaign.Id,
            Number = 1,
            CreatedAt = now,
            RuleJson = Rule.Everyone().ToJson(),
            Channels = Channels.Both,
            StartAt = schedule.StartAt,
            PerMinute = schedule.PerMinute,
            QuietStartHour = schedule.QuietStartHour,
            QuietEndHour = schedule.QuietEndHour,
            HoldoutShare = schedule.HoldoutShare,
        });
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(ct);
        return campaign.Id;
    }

    public async Task<CampaignEditorModel> GetEditorModelAsync(Guid id, CancellationToken ct)
    {
        var campaign = await LoadAsync(id, ct);
        var version = Current(campaign);
        return new CampaignEditorModel(campaign.Id, campaign.Name, campaign.Status, version.Number, ToEdit(campaign.Name, version), BlastRadiusShare);
    }

    public static CampaignEdit ToEdit(string name, CampaignVersion version) =>
        new(name, version.RuleJson, version.Channels, version.Message, version.Schedule);

    /// <summary>
    /// A version is immutable, so an edit that changes anything writes the next one. The name lives
    /// on the campaign and not on the version, so renaming alone does not make a version.
    /// </summary>
    public async Task<int> SaveAsync(Guid id, CampaignEdit edit, CancellationToken ct)
    {
        var campaign = await LoadAsync(id, ct);
        if (campaign.Status is not CampaignStatus.Draft)
        {
            throw new InvalidOperationException($"{campaign.Name} is {campaign.Status} and only a draft can be edited");
        }
        var current = Current(campaign);
        var now = DateTimeOffset.UtcNow;
        // Both sides go through the serialiser, because the stored copy comes back from jsonb with
        // its whitespace gone and its keys reordered and would otherwise never compare equal.
        var ruleJson = Rule.FromJson(edit.RuleJson).ToJson();
        var changed = ruleJson != Rule.FromJson(current.RuleJson).ToJson()
            || edit.Channels != current.Channels
            || edit.Message != current.Message
            || edit.Schedule != current.Schedule;

        if (campaign.Name != edit.Name)
        {
            campaign.Name = edit.Name;
            campaign.UpdatedAt = now;
        }
        if (!changed)
        {
            await db.SaveChangesAsync(ct);
            return current.Number;
        }

        var number = campaign.CurrentVersion + 1;
        campaign.Versions.Add(new CampaignVersion
        {
            CampaignId = campaign.Id,
            Number = number,
            CreatedAt = now,
            RuleJson = ruleJson,
            Channels = edit.Channels,
            PushTitle = edit.Message.PushTitle,
            PushBody = edit.Message.PushBody,
            EmailSubject = edit.Message.EmailSubject,
            EmailBody = edit.Message.EmailBody,
            StartAt = edit.Schedule.StartAt,
            PerMinute = edit.Schedule.PerMinute,
            QuietStartHour = edit.Schedule.QuietStartHour,
            QuietEndHour = edit.Schedule.QuietEndHour,
            HoldoutShare = edit.Schedule.HoldoutShare,
        });
        campaign.CurrentVersion = number;
        campaign.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return number;
    }

    public async Task SubmitAsync(Guid id, CancellationToken ct)
    {
        var campaign = await LoadAsync(id, ct);
        if (campaign.Status is not CampaignStatus.Draft)
        {
            throw new InvalidOperationException($"{campaign.Name} is {campaign.Status} and only a draft can be submitted");
        }
        campaign.Status = CampaignStatus.InReview;
        campaign.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// One approval per reviewer per version. A campaign that reaches more of the base than the
    /// blast radius share needs two of them, anything smaller needs one.
    /// </summary>
    public async Task ApproveAsync(Guid id, string reviewer, CancellationToken ct)
    {
        var campaign = await LoadAsync(id, ct);
        if (campaign.Status is not CampaignStatus.InReview)
        {
            throw new InvalidOperationException($"{campaign.Name} is {campaign.Status} and only a campaign in review can be approved");
        }
        var version = Current(campaign);
        var now = DateTimeOffset.UtcNow;
        if (!campaign.Approvals.Any(a => a.Version == version.Number && a.Reviewer == reviewer))
        {
            campaign.Approvals.Add(new Approval { CampaignId = campaign.Id, Version = version.Number, Reviewer = reviewer, At = now });
        }
        var estimate = await EstimateAsync(version, ct);
        if (campaign.Approvals.Count(a => a.Version == version.Number) >= RequiredApprovals(estimate))
        {
            campaign.Status = CampaignStatus.Approved;
        }
        campaign.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(Guid id, CancellationToken ct)
    {
        var campaign = await LoadAsync(id, ct);
        if (campaign.Status is not CampaignStatus.InReview)
        {
            throw new InvalidOperationException($"{campaign.Name} is {campaign.Status} and only a campaign in review can be rejected");
        }
        campaign.Status = CampaignStatus.Draft;
        campaign.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public int RequiredApprovals(AudienceEstimate estimate) => estimate.Share > BlastRadiusShare ? 2 : 1;

    public async Task<AudienceEstimate> EstimateAsync(CampaignVersion version, CancellationToken ct)
    {
        var users = await audience.LoadAsync(ct);
        return AudienceEstimate.Compute(Rule.FromJson(version.RuleJson), users, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The outbox write. The rule runs over the cached snapshot, the planner turns the matched users
    /// into a due time each, and one transaction inserts every delivery row and moves the campaign to
    /// Scheduled. Either the whole send exists in the database or none of it does, so a crash here
    /// cannot leave a campaign that says Scheduled with nothing to send. Workers pick the rows up
    /// from there, which is why nothing is handed to a provider inside this method.
    /// </summary>
    public async Task ScheduleAsync(Guid id, CancellationToken ct)
    {
        var campaign = await LoadAsync(id, ct);
        if (campaign.Status is not CampaignStatus.Approved)
        {
            throw new InvalidOperationException($"{campaign.Name} is {campaign.Status} and only an approved campaign can be scheduled");
        }
        var version = Current(campaign);
        var now = DateTimeOffset.UtcNow;
        var users = await audience.LoadAsync(ct);
        var selected = AudienceEstimate.Select(Rule.FromJson(version.RuleJson), users, now).ToList();
        // An approval can sit for a day, so a start time in the past becomes now instead of a backlog.
        var schedule = version.Schedule.StartAt < now ? version.Schedule with { StartAt = now } : version.Schedule;
        var plan = RolloutPlanner.Plan(campaign.Id, selected, schedule);
        var channels = new[] { Channel.Push, Channel.Email }
            .Where(c => version.Channels.HasFlag(c is Channel.Push ? Channels.Push : Channels.Email))
            .ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using (var writer = await connection.BeginBinaryImportAsync(
            "COPY deliveries (campaign_id, user_id, channel, status, skip_reason, due_at, attempts) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var planned in plan)
            {
                foreach (var channel in channels)
                {
                    await writer.StartRowAsync(ct);
                    await writer.WriteAsync(campaign.Id, NpgsqlDbType.Uuid, ct);
                    await writer.WriteAsync(planned.UserId, NpgsqlDbType.Integer, ct);
                    await writer.WriteAsync(channel.ToString(), NpgsqlDbType.Text, ct);
                    await writer.WriteAsync((planned.Holdout ? DeliveryStatus.Holdout : DeliveryStatus.Queued).ToString(), NpgsqlDbType.Text, ct);
                    await writer.WriteAsync(SkipReason.None.ToString(), NpgsqlDbType.Text, ct);
                    await writer.WriteAsync(planned.DueAt, NpgsqlDbType.TimestampTz, ct);
                    await writer.WriteAsync(0, NpgsqlDbType.Integer, ct);
                }
            }
            await writer.CompleteAsync(ct);
        }

        campaign.Status = CampaignStatus.Scheduled;
        campaign.ScheduledAt = schedule.StartAt;
        campaign.AudienceSize = plan.Count;
        campaign.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyDictionary<DeliveryStatus, int>> CountsByStatusAsync(Guid id, CancellationToken ct) =>
        await db.Deliveries.AsNoTracking()
            .Where(d => d.CampaignId == id)
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

    public async Task<CampaignResults> ResultsAsync(Guid id, CancellationToken ct)
    {
        var campaign = await db.Campaigns.AsNoTracking().FirstAsync(c => c.Id == id, ct);
        var rows = db.Deliveries.AsNoTracking().Where(d => d.CampaignId == id);

        var byStatus = await rows.GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        var skips = await rows.Where(d => d.Status == DeliveryStatus.Skipped)
            .GroupBy(d => d.SkipReason)
            .Select(g => new { Reason = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Reason, x => x.Count, ct);

        var channelCounts = await rows.GroupBy(d => d.Channel)
            .Select(g => new
            {
                Channel = g.Key,
                Rows = g.Count(),
                Sent = g.Count(d => d.SentAt != null),
                Delivered = g.Count(d => d.DeliveredAt != null),
                Opened = g.Count(d => d.OpenedAt != null),
                Failed = g.Count(d => d.Status == DeliveryStatus.Failed),
                Skipped = g.Count(d => d.Status == DeliveryStatus.Skipped),
            })
            .ToListAsync(ct);
        var byChannel = channelCounts
            .Select(c => new ChannelResult(c.Channel, c.Rows, c.Sent, c.Delivered, c.Opened, c.Failed, c.Skipped))
            .OrderBy(c => c.Channel)
            .ToList();

        var providerCounts = await rows.Where(d => d.Provider != null)
            .GroupBy(d => d.Provider!)
            .Select(g => new
            {
                Provider = g.Key,
                Sent = g.Count(d => d.SentAt != null),
                Failed = g.Count(d => d.Status == DeliveryStatus.Failed),
            })
            .ToListAsync(ct);
        var byProvider = providerCounts
            .Select(p => new ProviderResult(p.Provider, p.Sent, p.Failed))
            .OrderBy(p => p.Provider, StringComparer.Ordinal)
            .ToList();

        var deadRows = await rows.Where(d => d.Status == DeliveryStatus.Failed)
            .OrderBy(d => d.UserId)
            .Take(200)
            .Select(d => new { d.UserId, d.Channel, d.Provider, d.Attempts, d.LastError })
            .ToListAsync(ct);
        var dead = deadRows.Select(d => new DeadLetter(d.UserId, d.Channel, d.Provider, d.Attempts, d.LastError)).ToList();

        return new CampaignResults(
            campaign.AudienceSize ?? 0,
            byStatus.Values.Sum(),
            byStatus,
            skips,
            byChannel,
            byProvider,
            byStatus.GetValueOrDefault(DeliveryStatus.Holdout),
            dead);
    }
}

/// <summary>
/// What the editor talks to while it renders on the server. The browser copy of the editor calls
/// the JSON endpoints instead, and both end up in <see cref="CampaignService"/>.
/// </summary>
public sealed class ServerCampaignEditorApi(CampaignService campaigns) : ICampaignEditorApi
{
    public Task<CampaignEditorModel> GetAsync(Guid id, CancellationToken ct = default) => campaigns.GetEditorModelAsync(id, ct);

    public Task<int> SaveAsync(Guid id, CampaignEdit edit, CancellationToken ct = default) => campaigns.SaveAsync(id, edit, ct);

    public Task SubmitAsync(Guid id, CancellationToken ct = default) => campaigns.SubmitAsync(id, ct);
}
