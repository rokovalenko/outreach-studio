using OutreachStudio.Engine.Rules;

namespace OutreachStudio.Engine.Audience;

public enum AttributeType { String, Number, Date, Enum, Bool }

/// <summary>An attribute a rule can compare or a message can interpolate.</summary>
public sealed record AttributeDefinition(
    string Name,
    string Label,
    AttributeType Type,
    IReadOnlyList<string> EnumValues,
    Func<AudienceUser, object> Read)
{
    public IReadOnlyList<CompareOp> Ops => Type switch
    {
        AttributeType.String => [CompareOp.Eq, CompareOp.NotEq, CompareOp.In, CompareOp.NotIn],
        AttributeType.Enum => [CompareOp.Eq, CompareOp.NotEq, CompareOp.In, CompareOp.NotIn],
        AttributeType.Number => [CompareOp.Eq, CompareOp.NotEq, CompareOp.Gt, CompareOp.Gte, CompareOp.Lt, CompareOp.Lte],
        AttributeType.Date => [CompareOp.Before, CompareOp.After, CompareOp.WithinLastDays, CompareOp.NotWithinLastDays],
        AttributeType.Bool => [CompareOp.Eq],
        _ => [],
    };
}

/// <summary>
/// The one list of attributes and event types. The rule builder renders inputs from it, the
/// evaluator reads values through it and the token validator checks message templates against it.
/// </summary>
public static class UserSchema
{
    public static readonly IReadOnlyList<AttributeDefinition> Attributes =
    [
        new("country", "Country", AttributeType.String, [], u => u.Country),
        new("tier", "Tier", AttributeType.Enum, Enum.GetNames<Tier>(), u => u.Tier.ToString()),
        new("signup_date", "Signup date", AttributeType.Date, [], u => u.SignupDate),
        new("last_active", "Last active", AttributeType.Date, [], u => u.LastActive),
        new("platform", "Platform", AttributeType.Enum, Enum.GetNames<Platform>(), u => u.Platform.ToString()),
        new("marketing_consent", "Marketing consent", AttributeType.Bool, [], u => u.MarketingConsent),
        new("time_zone", "Time zone", AttributeType.String, [], u => u.TimeZone),
        new("points_balance", "Points balance", AttributeType.Number, [], u => u.PointsBalance),
    ];

    /// <summary>Tokens a message template may use. A subset of the attributes plus the first name.</summary>
    public static readonly IReadOnlyDictionary<string, Func<AudienceUser, string>> Tokens =
        new Dictionary<string, Func<AudienceUser, string>>
        {
            ["first_name"] = u => u.FirstName,
            ["tier"] = u => u.Tier.ToString(),
            ["points_balance"] = u => u.PointsBalance.ToString(),
            ["country"] = u => u.Country,
        };

    public static readonly IReadOnlyList<(EventType Type, string Label)> Events =
    [
        (EventType.PointsEarned, "Points earned"),
        (EventType.RewardClaimed, "Reward claimed"),
        (EventType.MissionCompleted, "Mission completed"),
        (EventType.Purchase, "Purchase"),
        (EventType.Login, "Login"),
        (EventType.CampaignOpened, "Campaign opened"),
    ];

    public static AttributeDefinition? Find(string name) =>
        Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
}
