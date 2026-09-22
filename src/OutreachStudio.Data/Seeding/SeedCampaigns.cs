using OutreachStudio.Engine.Audience;
using OutreachStudio.Engine.Campaigns;
using OutreachStudio.Engine.Rules;

namespace OutreachStudio.Data.Seeding;

/// <summary>How the seeded deliveries of a finished campaign turned out.</summary>
public enum Outcome
{
    /// <summary>Both providers healthy.</summary>
    Healthy,
    /// <summary>The email provider was down for part of the send, a third of emails dead-lettered.</summary>
    EmailOutage,
    /// <summary>Both push providers rejected almost everything.</summary>
    PushCollapse,
}

public sealed record SeedCampaign(
    string Name,
    CampaignStatus Status,
    int DaysAgo,
    Rule Rule,
    Channels Channels,
    string PushTitle,
    string PushBody,
    string EmailSubject,
    string EmailBody,
    Outcome Outcome = Outcome.Healthy,
    int PerMinute = 600,
    double HoldoutShare = 0.05,
    int Approvals = 1);

/// <summary>
/// The campaign history on a fresh database: every status is represented and two sends went wrong
/// in different ways, so the dashboard, the results page and the provider tiles have something to show.
/// </summary>
public static class SeedCampaigns
{
    public static IReadOnlyList<SeedCampaign> All =>
    [
        new("Gold reactivation", CampaignStatus.Done, 34,
            new AndRule([
                new AttributeCompare("tier", CompareOp.In, "Gold,Platinum"),
                new AttributeCompare("last_active", CompareOp.NotWithinLastDays, "14"),
            ]),
            Channels.Both,
            "We kept your seat warm, {{first_name}}",
            "Your {{tier}} perks are waiting. Come back this week and earn double points.",
            "{{first_name}}, your {{tier}} perks are waiting",
            "Hi {{first_name}},\n\nIt has been a while. Your {{points_balance}} points are safe and this week every mission pays double.\n\nSee you inside."),

        new("Weekend mission boost", CampaignStatus.Done, 27,
            new AndRule([
                new EventCountInWindow(EventType.MissionCompleted, CompareOp.Gte, 1, 30),
                new AttributeCompare("marketing_consent", CompareOp.Eq, "true"),
            ]),
            Channels.Push,
            "Weekend missions are live",
            "{{first_name}}, finish any mission before Sunday night for a 25% points boost.",
            "", "",
            PerMinute: 1200),

        new("Points expiry notice", CampaignStatus.Done, 21,
            new AndRule([
                new AttributeCompare("points_balance", CompareOp.Gte, "2000"),
                new EventCountInWindow(EventType.Login, CompareOp.Lte, 1, 30),
            ]),
            Channels.Email,
            "", "",
            "{{points_balance}} points expire soon",
            "Hi {{first_name}},\n\nYou have {{points_balance}} points and some of them expire at the end of the month. Claim a reward before they go.",
            Outcome.EmailOutage, Approvals: 2),

        new("Android launch offer", CampaignStatus.Done, 16,
            new AndRule([
                new AttributeCompare("platform", CompareOp.Eq, "Android"),
                new AttributeCompare("signup_date", CompareOp.WithinLastDays, "90"),
            ]),
            Channels.Push,
            "Welcome to the new app",
            "{{first_name}}, the Android app just got a rewards tab. Open it to claim 200 points.",
            "", "",
            Outcome.PushCollapse, Approvals: 2),

        new("Platinum thank you", CampaignStatus.Done, 11,
            new AttributeCompare("tier", CompareOp.Eq, "Platinum"),
            Channels.Both,
            "Thank you, {{first_name}}",
            "Platinum members get early access to next month's rewards catalogue. Take a look.",
            "Early access to next month's rewards",
            "Hi {{first_name}},\n\nAs a Platinum member you can browse and reserve next month's rewards from today.",
            HoldoutShare: 0),

        new("Polish market survey", CampaignStatus.Done, 6,
            new AndRule([
                new AttributeCompare("country", CompareOp.Eq, "PL"),
                new EventCountInWindow(EventType.Purchase, CompareOp.Gte, 1, 60),
            ]),
            Channels.Email,
            "", "",
            "Two minutes, 300 points",
            "Hi {{first_name}},\n\nTell us what you think of the rewards catalogue and we add 300 points to your balance.",
            PerMinute: 300),

        new("Lapsed Bronze win-back", CampaignStatus.Failed, 4,
            new AndRule([
                new AttributeCompare("tier", CompareOp.Eq, "Bronze"),
                new AttributeCompare("last_active", CompareOp.NotWithinLastDays, "45"),
            ]),
            Channels.Push,
            "We miss you",
            "{{first_name}}, log in this week and your first mission pays triple.",
            "", "",
            Outcome.PushCollapse),

        new("Weekly mission reminder", CampaignStatus.Scheduled, 0,
            new AndRule([
                new EventCountInWindow(EventType.Login, CompareOp.Gte, 2, 14),
                new EventCountInWindow(EventType.MissionCompleted, CompareOp.Eq, 0, 7),
                new AttributeCompare("marketing_consent", CompareOp.Eq, "true"),
            ]),
            Channels.Push,
            "A mission is waiting",
            "{{first_name}}, you have not finished a mission this week. The quickest one takes two minutes.",
            "", "",
            PerMinute: 900),

        new("Silver upgrade nudge", CampaignStatus.Approved, 1,
            new AndRule([
                new AttributeCompare("tier", CompareOp.Eq, "Silver"),
                new AttributeCompare("points_balance", CompareOp.Gte, "1500"),
                new EventCountInWindow(EventType.RewardClaimed, CompareOp.Gte, 1, 60),
            ]),
            Channels.Both,
            "Gold is closer than you think",
            "{{first_name}}, you are one reward away from Gold. Claim anything this month to upgrade.",
            "One reward away from Gold",
            "Hi {{first_name}},\n\nYour {{points_balance}} points put you within reach of Gold. Claim any reward this month and we upgrade you on the spot.",
            Approvals: 1),

        new("Brazil catalogue launch", CampaignStatus.InReview, 1,
            new AttributeCompare("country", CompareOp.Eq, "BR"),
            Channels.Both,
            "New rewards for Brazil",
            "{{first_name}}, the catalogue now has local partners. Have a look.",
            "The rewards catalogue is now local",
            "Oi {{first_name}},\n\nThe catalogue now includes partners in Brazil. Browse the new section today.",
            Approvals: 0),

        new("iOS survey", CampaignStatus.Draft, 2,
            new AndRule([
                new AttributeCompare("platform", CompareOp.Eq, "Ios"),
                new EventCountInWindow(EventType.CampaignOpened, CompareOp.Gte, 1, 30),
            ]),
            Channels.Email,
            "", "",
            "How is the app treating you?",
            "Hi {{first_name}},\n\nA two minute survey about the iOS app, 150 points on completion.",
            Approvals: 0),

        new("Spring double points", CampaignStatus.Draft, 0,
            Rule.Everyone(),
            Channels.Both,
            "", "", "", "",
            Approvals: 0),
    ];
}
