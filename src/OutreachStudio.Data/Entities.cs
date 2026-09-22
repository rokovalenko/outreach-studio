using OutreachStudio.Engine.Audience;
using OutreachStudio.Engine.Campaigns;
using OutreachStudio.Engine.Messaging;
using OutreachStudio.Engine.Scheduling;

namespace OutreachStudio.Data;

public enum DeliveryStatus { Queued, Claimed, Sent, Delivered, Opened, Failed, Skipped, Holdout }

public enum SkipReason { None, ConsentRevoked, Suppressed, FrequencyCap }

public sealed class User
{
    public int Id { get; set; }
    public required string FirstName { get; set; }
    public required string Email { get; set; }
    public required string Country { get; set; }
    public Tier Tier { get; set; }
    public DateOnly SignupDate { get; set; }
    public DateOnly LastActive { get; set; }
    public Platform Platform { get; set; }
    public bool MarketingConsent { get; set; }
    public required string TimeZone { get; set; }
    public int PointsBalance { get; set; }

    public AudienceUser ToAudienceUser(IReadOnlyDictionary<EventType, long[]>? events = null) =>
        new(Id, FirstName, Email, Country, Tier, SignupDate, LastActive, Platform, MarketingConsent, TimeZone, PointsBalance,
            events ?? new Dictionary<EventType, long[]>());
}

public sealed class UserEvent
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public EventType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class Campaign
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public CampaignStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>The version that was submitted, approved and sent. Drafts edit a new version on top.</summary>
    public int CurrentVersion { get; set; }
    /// <summary>Filled at schedule time from the server-side evaluation.</summary>
    public int? AudienceSize { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public List<CampaignVersion> Versions { get; } = [];
    public List<Approval> Approvals { get; } = [];
}

/// <summary>Immutable once written. A diff between two versions is a diff between two of these rows.</summary>
public sealed class CampaignVersion
{
    public Guid CampaignId { get; set; }
    public int Number { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string RuleJson { get; set; }
    public Channels Channels { get; set; } = Channels.Both;
    public string PushTitle { get; set; } = "";
    public string PushBody { get; set; } = "";
    public string EmailSubject { get; set; } = "";
    public string EmailBody { get; set; } = "";
    public DateTimeOffset StartAt { get; set; }
    public int PerMinute { get; set; } = 600;
    public int QuietStartHour { get; set; } = 22;
    public int QuietEndHour { get; set; } = 8;
    public double HoldoutShare { get; set; } = 0.05;

    public MessageTemplate Message => new(PushTitle, PushBody, EmailSubject, EmailBody);
    public Schedule Schedule => new(StartAt, PerMinute, QuietStartHour, QuietEndHour, HoldoutShare);
}

public sealed class Approval
{
    public int Id { get; set; }
    public Guid CampaignId { get; set; }
    public int Version { get; set; }
    public required string Reviewer { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// The outbox. One row per user per campaign per channel, enforced by a unique index, written in the
/// same transaction that moves the campaign to Scheduled. Workers claim rows with SKIP LOCKED.
/// </summary>
public sealed class Delivery
{
    public long Id { get; set; }
    public Guid CampaignId { get; set; }
    public int UserId { get; set; }
    public Channel Channel { get; set; }
    public DeliveryStatus Status { get; set; }
    public SkipReason SkipReason { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public int Attempts { get; set; }
    public string? Provider { get; set; }
    public string? LastError { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    /// <summary>The id the provider returned, so receipts can find the row.</summary>
    public string? ProviderMessageId { get; set; }
}

/// <summary>One provider call. Provider health tiles are an aggregate over the recent rows.</summary>
public sealed class DeliveryAttempt
{
    public long Id { get; set; }
    public long DeliveryId { get; set; }
    public required string Provider { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public int LatencyMs { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
}

public sealed class Suppression
{
    public required string Email { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
