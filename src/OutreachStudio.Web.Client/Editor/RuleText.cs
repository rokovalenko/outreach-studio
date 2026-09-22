using System.Globalization;
using OutreachStudio.Engine.Audience;
using OutreachStudio.Engine.Rules;

namespace OutreachStudio.Web.Client.Editor;

/// <summary>One clause read as a sentence. Depth is how deep the clause sits in the tree.</summary>
public sealed record RuleLine(int Depth, string Text);

/// <summary>
/// Operator labels and the sentence form of a rule. The condition rows, the match explanation and
/// the review summary all read from here, so the three cannot drift apart.
/// </summary>
public static class RuleText
{
    /// <summary>The count comparisons an event clause may use, in the order the select shows them.</summary>
    public static readonly IReadOnlyList<CompareOp> CountOps =
        [CompareOp.Gte, CompareOp.Lte, CompareOp.Eq, CompareOp.Gt, CompareOp.Lt, CompareOp.NotEq];

    /// <summary>The label of an attribute operator in the select.</summary>
    public static string OperatorLabel(CompareOp op) => op switch
    {
        CompareOp.Eq => "is",
        CompareOp.NotEq => "is not",
        CompareOp.In => "one of",
        CompareOp.NotIn => "not one of",
        CompareOp.Gt => "greater than",
        CompareOp.Gte => "at least",
        CompareOp.Lt => "less than",
        CompareOp.Lte => "at most",
        CompareOp.Before => "before",
        CompareOp.After => "after",
        CompareOp.WithinLastDays => "within last N days",
        CompareOp.NotWithinLastDays => "not within last N days",
        _ => op.ToString(),
    };

    /// <summary>The label of a count operator on an event clause.</summary>
    public static string CountOperatorLabel(CompareOp op) => op switch
    {
        CompareOp.Gte => "at least",
        CompareOp.Lte => "at most",
        CompareOp.Eq => "exactly",
        CompareOp.Gt => "more than",
        CompareOp.Lt => "fewer than",
        CompareOp.NotEq => "not equal",
        _ => op.ToString(),
    };

    public static string EventLabel(EventType type)
    {
        foreach (var known in UserSchema.Events)
        {
            if (known.Type == type)
            {
                return known.Label;
            }
        }

        return type.ToString();
    }

    public static string AttributeLabel(string name) => UserSchema.Find(name)?.Label ?? name;

    /// <summary>The whole tree as indented sentences, for the review summary.</summary>
    public static IReadOnlyList<RuleLine> Lines(Rule rule)
    {
        var lines = new List<RuleLine>();
        Walk(rule, 0, lines);
        return lines;
    }

    /// <summary>One clause in words, without its children.</summary>
    public static string Clause(Rule rule) => rule switch
    {
        AndRule and => and.Children.Count == 0 ? "everyone" : "all of the following",
        OrRule or => or.Children.Count == 0 ? "nobody" : "any of the following",
        NotRule => "not",
        AttributeCompare a => Attribute(a),
        EventCountInWindow e => Event(e),
        _ => rule.GetType().Name,
    };

    private static void Walk(Rule rule, int depth, List<RuleLine> lines)
    {
        lines.Add(new(depth, Clause(rule)));
        switch (rule)
        {
            case AndRule and:
                foreach (var child in and.Children)
                {
                    Walk(child, depth + 1, lines);
                }

                break;
            case OrRule or:
                foreach (var child in or.Children)
                {
                    Walk(child, depth + 1, lines);
                }

                break;
            case NotRule not:
                Walk(not.Child, depth + 1, lines);
                break;
        }
    }

    private static string Attribute(AttributeCompare a)
    {
        var def = UserSchema.Find(a.Attribute);
        var label = def?.Label ?? a.Attribute;
        return a.Op switch
        {
            CompareOp.In => $"{label} one of {string.Join(", ", a.Values)}",
            CompareOp.NotIn => $"{label} not one of {string.Join(", ", a.Values)}",
            CompareOp.Before => $"{label} before {a.Value}",
            CompareOp.After => $"{label} after {a.Value}",
            CompareOp.WithinLastDays => $"{label} in the last {a.Value} days",
            CompareOp.NotWithinLastDays => $"{label} not in the last {a.Value} days",
            CompareOp.Eq when def?.Type == AttributeType.Bool => $"{label} is {YesNo(a.Value)}",
            _ => $"{label} {OperatorLabel(a.Op)} {a.Value}",
        };
    }

    private static string YesNo(string value) => bool.TryParse(value, out var flag) && flag ? "yes" : "no";

    private static string Event(EventCountInWindow e)
    {
        var count = e.Count.ToString(CultureInfo.InvariantCulture);
        var times = e.Count == 1 ? "time" : "times";
        var days = e.WindowDays.ToString(CultureInfo.InvariantCulture);
        return $"{EventLabel(e.Event)} {CountOperatorLabel(e.Op)} {count} {times} in the last {days} days";
    }
}
