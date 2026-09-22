using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OutreachStudio.Data;
using OutreachStudio.Engine.Messaging;
using OutreachStudio.Engine.Scheduling;

namespace OutreachStudio.Worker;

/// <summary>
/// What happens to one claimed delivery. Guardrails run here and not at enqueue time, because
/// consent, suppressions and the frequency cap can all change between scheduling and sending.
/// Every path leaves the row out of Claimed, so only a crash can strand one.
/// </summary>
public sealed class DeliveryPipeline(
    OutreachDbContext db,
    IProviderClient providers,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<DeliveryPipeline> logger)
{
    private const int PrimaryTries = 3;

    /// <summary>The wait before primary try 2 and try 3. The secondary is tried once, straight away.</summary>
    private static readonly TimeSpan[] PrimaryBackoff = [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(800)];

    public async Task ProcessAsync(Delivery delivery, CancellationToken ct)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        db.Attach(delivery);

        using var activity = Telemetry.Source.StartActivity($"deliver {delivery.Channel}");
        activity?.SetTag("campaign.id", delivery.CampaignId);
        activity?.SetTag("delivery.id", delivery.Id);
        activity?.SetTag("user.id", delivery.UserId);
        activity?.SetTag("delivery.channel", delivery.Channel.ToString());
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["DeliveryId"] = delivery.Id,
            ["CampaignId"] = delivery.CampaignId,
            ["UserId"] = delivery.UserId,
        });

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == delivery.UserId, ct);
        var campaign = await db.Campaigns.AsNoTracking().FirstAsync(c => c.Id == delivery.CampaignId, ct);
        var version = await db.CampaignVersions.AsNoTracking()
            .FirstAsync(v => v.CampaignId == campaign.Id && v.Number == campaign.CurrentVersion, ct);

        if (!user.MarketingConsent)
        {
            await SkipAsync(delivery, SkipReason.ConsentRevoked, activity, ct);
            return;
        }

        if (await db.Suppressions.AnyAsync(s => s.Email == user.Email, ct))
        {
            await SkipAsync(delivery, SkipReason.Suppressed, activity, ct);
            return;
        }

        var windowStart = now.AddDays(-settings.FrequencyWindowDays);
        var recentSends = await db.Deliveries.CountAsync(
            d => d.UserId == delivery.UserId && d.CampaignId != delivery.CampaignId && d.SentAt >= windowStart, ct);
        if (recentSends >= settings.FrequencyCap)
        {
            await SkipAsync(delivery, SkipReason.FrequencyCap, activity, ct);
            return;
        }

        var allowed = QuietHours.NextAllowed(now, user.TimeZone, version.Schedule);
        if (allowed > now)
        {
            await DeferAsync(delivery, allowed, activity, ct);
            return;
        }

        await SendAsync(delivery, user, version, activity, now, settings, ct);
    }

    private async Task SkipAsync(Delivery delivery, SkipReason reason, Activity? activity, CancellationToken ct)
    {
        delivery.Status = DeliveryStatus.Skipped;
        delivery.SkipReason = reason;
        await db.SaveChangesAsync(ct);

        activity?.SetTag("delivery.outcome", $"skipped:{Slug(reason)}");
        Count(DeliveryStatus.Skipped, delivery.Channel, null, Slug(reason));
        logger.LogInformation("Delivery {DeliveryId} skipped by the {Reason} guardrail", delivery.Id, reason);
    }

    private async Task DeferAsync(Delivery delivery, DateTimeOffset allowed, Activity? activity, CancellationToken ct)
    {
        delivery.Status = DeliveryStatus.Queued;
        delivery.DueAt = allowed;
        Unclaim(delivery);
        await db.SaveChangesAsync(ct);

        activity?.SetTag("delivery.outcome", "deferred");
        Count(DeliveryStatus.Queued, delivery.Channel, null, "quiet_hours");
        logger.LogInformation("Delivery {DeliveryId} deferred to {DueAt} by quiet hours", delivery.Id, allowed);
    }

    private async Task SendAsync(
        Delivery delivery,
        User user,
        CampaignVersion version,
        Activity? activity,
        DateTimeOffset now,
        OutboxOptions settings,
        CancellationToken ct)
    {
        var message = Personalisation.Render(version.Message, delivery.Channel, user.ToAudienceUser());
        var to = delivery.Channel == Channel.Email ? user.Email : $"device:{user.Id}";
        var request = new SendRequest(delivery.Id, to, message.Title, message.Body);
        var (primary, secondary) = Route(delivery.Channel);

        var lastError = "no provider was reached";
        var provider = primary;
        for (var attempt = 1; attempt <= PrimaryTries + 1; attempt++)
        {
            var onSecondary = attempt > PrimaryTries;
            provider = onSecondary ? secondary : primary;
            if (attempt > 1 && !onSecondary)
            {
                await Task.Delay(PrimaryBackoff[attempt - 2], clock, ct);
            }

            var result = await CallAsync(delivery, provider, attempt, request, ct);
            if (result.Succeeded)
            {
                delivery.Status = DeliveryStatus.Sent;
                delivery.Provider = provider;
                delivery.ProviderMessageId = result.MessageId;
                delivery.SentAt = now;
                delivery.Attempts += 1;
                delivery.LastError = null;
                await db.SaveChangesAsync(ct);

                Tag(activity, provider, delivery.Attempts, "sent");
                Count(DeliveryStatus.Sent, delivery.Channel, provider, null);
                logger.LogInformation("Delivery {DeliveryId} sent through {Provider} on try {Try}", delivery.Id, provider, attempt);
                return;
            }
            lastError = result.Error ?? "provider call failed";
        }

        delivery.Attempts += 1;
        delivery.LastError = lastError;
        delivery.Provider = provider;
        var dead = delivery.Attempts >= settings.MaxAttempts;
        if (dead)
        {
            delivery.Status = DeliveryStatus.Failed;
        }
        else
        {
            delivery.Status = DeliveryStatus.Queued;
            delivery.DueAt = now + TimeSpan.FromSeconds(10 * Math.Pow(3, delivery.Attempts - 1));
            Unclaim(delivery);
        }
        await db.SaveChangesAsync(ct);

        Tag(activity, provider, delivery.Attempts, dead ? "dead_letter" : "retry");
        Count(dead ? DeliveryStatus.Failed : DeliveryStatus.Queued, delivery.Channel, provider, dead ? "dead_letter" : "retry");
        logger.LogWarning("Delivery {DeliveryId} failed on attempt {Attempt}, {Error}", delivery.Id, delivery.Attempts, lastError);
    }

    private async Task<ProviderResult> CallAsync(Delivery delivery, string provider, int attempt, SendRequest request, CancellationToken ct)
    {
        using var span = Telemetry.Source.StartActivity($"send {provider}");
        span?.SetTag("delivery.id", delivery.Id);
        span?.SetTag("delivery.provider", provider);
        span?.SetTag("provider.try", attempt);

        var startedAt = clock.GetUtcNow();
        var started = Stopwatch.GetTimestamp();
        ProviderResult result;
        try
        {
            result = await providers.SendAsync(provider, request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            result = ProviderResult.Failed(ex.Message);
        }
        var latency = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        Telemetry.ProviderLatency.Record(latency, new KeyValuePair<string, object?>("provider", provider));
        span?.SetStatus(result.Succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error, result.Error);
        db.DeliveryAttempts.Add(new DeliveryAttempt
        {
            DeliveryId = delivery.Id,
            Provider = provider,
            StartedAt = startedAt,
            LatencyMs = (int)latency,
            Succeeded = result.Succeeded,
            Error = result.Error,
        });
        return result;
    }

    /// <summary>The a providers take the traffic, the b providers are the failover target.</summary>
    private static (string Primary, string Secondary) Route(Channel channel) => channel switch
    {
        Channel.Push => ("push-a", "push-b"),
        Channel.Email => ("email-a", "email-b"),
        _ => throw new NotSupportedException(channel.ToString()),
    };

    private static void Unclaim(Delivery delivery)
    {
        delivery.ClaimedBy = null;
        delivery.ClaimedAt = null;
    }

    private static void Tag(Activity? activity, string provider, int attempt, string outcome)
    {
        activity?.SetTag("delivery.provider", provider);
        activity?.SetTag("delivery.attempt", attempt);
        activity?.SetTag("delivery.outcome", outcome);
    }

    private static void Count(DeliveryStatus status, Channel channel, string? provider, string? reason)
    {
        var tags = new TagList
        {
            { "status", status.ToString() },
            { "channel", channel.ToString() },
            { "provider", provider },
            { "reason", reason },
        };
        Telemetry.Deliveries.Add(1, tags);
    }

    private static string Slug(SkipReason reason) => reason switch
    {
        SkipReason.ConsentRevoked => "consent_revoked",
        SkipReason.Suppressed => "suppressed",
        SkipReason.FrequencyCap => "frequency_cap",
        _ => reason.ToString(),
    };
}
