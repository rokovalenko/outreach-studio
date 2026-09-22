using OutreachStudio.Data;
using OutreachStudio.Engine.Audience;
using OutreachStudio.Engine.Campaigns;
using OutreachStudio.Engine.Messaging;
using OutreachStudio.Engine.Rules;

namespace OutreachStudio.Worker.Tests;

/// <summary>Rows a test needs, with everything the worker does not look at filled in once.</summary>
public static class Build
{
    public static Campaign Campaign(CampaignStatus status = CampaignStatus.Scheduled, int quietStart = 22, int quietEnd = 8)
    {
        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            Name = "Test campaign",
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
            CurrentVersion = 1,
            ScheduledAt = now,
        };
        campaign.Versions.Add(new CampaignVersion
        {
            CampaignId = campaign.Id,
            Number = 1,
            CreatedAt = now,
            RuleJson = Rule.Everyone().ToJson(),
            Channels = Channels.Both,
            PushTitle = "Hello {{first_name}}",
            PushBody = "You have {{points_balance}} points",
            EmailSubject = "Hello {{first_name}}",
            EmailBody = "You have {{points_balance}} points",
            StartAt = now,
            QuietStartHour = quietStart,
            QuietEndHour = quietEnd,
        });
        return campaign;
    }

    public static User User(bool consent = true, string timeZone = "UTC") => new()
    {
        FirstName = "Ada",
        Email = $"{Guid.NewGuid():N}@example.test",
        Country = "PL",
        Tier = Tier.Gold,
        SignupDate = new DateOnly(2025, 1, 1),
        LastActive = DateOnly.FromDateTime(DateTime.UtcNow),
        Platform = Platform.Ios,
        MarketingConsent = consent,
        TimeZone = timeZone,
        PointsBalance = 120,
    };

    public static Delivery Delivery(Campaign campaign, User user, Channel channel = Channel.Push, DateTimeOffset? dueAt = null) => new()
    {
        CampaignId = campaign.Id,
        UserId = user.Id,
        Channel = channel,
        Status = DeliveryStatus.Queued,
        DueAt = dueAt ?? DateTimeOffset.UtcNow.AddMinutes(-1),
    };

    /// <summary>One campaign, one user, one queued delivery, which is what most of these tests need.</summary>
    public static async Task<Seeded> OneAsync(
        PostgresFixture fixture,
        CancellationToken ct,
        bool consent = true,
        string timeZone = "UTC",
        Channel channel = Channel.Push,
        int quietStart = 22,
        int quietEnd = 8)
    {
        await using var db = fixture.NewContext();
        var campaign = Campaign(quietStart: quietStart, quietEnd: quietEnd);
        var user = User(consent, timeZone);
        db.Campaigns.Add(campaign);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var delivery = Delivery(campaign, user, channel);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(ct);
        return new Seeded(campaign.Id, user.Id, delivery.Id);
    }
}

public sealed record Seeded(Guid CampaignId, int UserId, long DeliveryId);
