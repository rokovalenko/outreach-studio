using OutreachStudio.Engine.Audience;
using OutreachStudio.Engine.Campaigns;
using OutreachStudio.Engine.Rules;

namespace OutreachStudio.Web.Services;

/// <summary>
/// A rule tree in words, for the read-only summary on the campaign page. The editor has its own
/// builder, this is the version a reviewer reads before approving.
/// </summary>
public static class RuleWords
{
    public static string Leaf(Rule rule) => rule switch
    {
        AttributeCompare a => $"{Label(a.Attribute)} {Op(a.Op)} {Value(a)}",
        EventCountInWindow e => $"{EventLabel(e.Event)} {Count(e.Op)} {e.Count} times in the last {e.WindowDays} days",
        _ => rule.GetType().Name,
    };

    public static string Describe(Channels channels) => channels switch
    {
        Engine.Campaigns.Channels.Both => "Push and email",
        Engine.Campaigns.Channels.Push => "Push",
        Engine.Campaigns.Channels.Email => "Email",
        _ => "No channel",
    };

    private static string Label(string attribute) => UserSchema.Find(attribute)?.Label ?? attribute;

    private static string EventLabel(EventType type) =>
        UserSchema.Events.FirstOrDefault(e => e.Type == type).Label ?? type.ToString();

    private static string Value(AttributeCompare a) => a.Op is CompareOp.WithinLastDays or CompareOp.NotWithinLastDays
        ? $"{a.Value} days"
        : a.Op is CompareOp.In or CompareOp.NotIn
            ? string.Join(", ", a.Values)
            : a.Value;

    private static string Op(CompareOp op) => op switch
    {
        CompareOp.Eq => "is",
        CompareOp.NotEq => "is not",
        CompareOp.Gt => "is more than",
        CompareOp.Gte => "is at least",
        CompareOp.Lt => "is less than",
        CompareOp.Lte => "is at most",
        CompareOp.In => "is one of",
        CompareOp.NotIn => "is not one of",
        CompareOp.Before => "is before",
        CompareOp.After => "is after",
        CompareOp.WithinLastDays => "is within the last",
        CompareOp.NotWithinLastDays => "is not within the last",
        _ => op.ToString(),
    };

    private static string Count(CompareOp op) => op switch
    {
        CompareOp.Eq => "exactly",
        CompareOp.NotEq => "not",
        CompareOp.Gt => "more than",
        CompareOp.Gte => "at least",
        CompareOp.Lt => "fewer than",
        CompareOp.Lte => "at most",
        _ => op.ToString(),
    };
}
