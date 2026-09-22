using System.Globalization;
using OutreachStudio.Engine.Audience;

namespace OutreachStudio.Engine.Rules;

/// <summary>One clause's outcome for one user. The tree of these is the match explanation.</summary>
public sealed record ClauseResult(Rule Clause, bool Matched, string Actual, IReadOnlyList<ClauseResult> Children);

/// <summary>
/// Interprets a rule tree against one user. <see cref="Matches"/> is the fast path for counting,
/// <see cref="Explain"/> keeps every clause's actual value for the explanation panel. Both run
/// unchanged in the browser and on the server, because the assembly has no dependencies.
/// </summary>
public static class RuleEvaluator
{
    public static bool Matches(Rule rule, AudienceUser user, DateTimeOffset now) => rule switch
    {
        AndRule and => and.Children.All(c => Matches(c, user, now)),
        OrRule or => or.Children.Count > 0 && or.Children.Any(c => Matches(c, user, now)),
        NotRule not => !Matches(not.Child, user, now),
        AttributeCompare a => CompareAttribute(a, user, now).Matched,
        EventCountInWindow e => CompareEvents(e, user, now).Matched,
        _ => throw new NotSupportedException(rule.GetType().Name),
    };

    public static ClauseResult Explain(Rule rule, AudienceUser user, DateTimeOffset now)
    {
        switch (rule)
        {
            case AndRule and:
            {
                var children = and.Children.Select(c => Explain(c, user, now)).ToList();
                return new(rule, children.All(c => c.Matched), $"{children.Count(c => c.Matched)} of {children.Count}", children);
            }
            case OrRule or:
            {
                var children = or.Children.Select(c => Explain(c, user, now)).ToList();
                return new(rule, children.Count > 0 && children.Any(c => c.Matched), $"{children.Count(c => c.Matched)} of {children.Count}", children);
            }
            case NotRule not:
            {
                var child = Explain(not.Child, user, now);
                return new(rule, !child.Matched, child.Actual, [child]);
            }
            case AttributeCompare a:
            {
                var (matched, actual) = CompareAttribute(a, user, now);
                return new(rule, matched, actual, []);
            }
            case EventCountInWindow e:
            {
                var (matched, actual) = CompareEvents(e, user, now);
                return new(rule, matched, actual, []);
            }
            default:
                throw new NotSupportedException(rule.GetType().Name);
        }
    }

    private static (bool Matched, string Actual) CompareEvents(EventCountInWindow e, AudienceUser user, DateTimeOffset now)
    {
        var count = user.CountEvents(e.Event, now.AddDays(-e.WindowDays), now);
        return (CompareNumbers(count, e.Op, e.Count), count.ToString(CultureInfo.InvariantCulture));
    }

    private static (bool Matched, string Actual) CompareAttribute(AttributeCompare a, AudienceUser user, DateTimeOffset now)
    {
        var def = UserSchema.Find(a.Attribute) ?? throw new InvalidOperationException($"Unknown attribute '{a.Attribute}'");
        var value = def.Read(user);
        var actual = value is DateOnly d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : value.ToString() ?? "";
        var matched = def.Type switch
        {
            AttributeType.Number => CompareNumbers(Convert.ToDouble(value, CultureInfo.InvariantCulture), a.Op, double.Parse(a.Value, CultureInfo.InvariantCulture)),
            AttributeType.Date => CompareDates((DateOnly)value, a, DateOnly.FromDateTime(now.UtcDateTime)),
            AttributeType.Bool => (bool)value == bool.Parse(a.Value),
            _ => CompareStrings(actual, a),
        };
        return (matched, actual);
    }

    private static bool CompareStrings(string actual, AttributeCompare a) => a.Op switch
    {
        CompareOp.Eq => string.Equals(actual, a.Value, StringComparison.OrdinalIgnoreCase),
        CompareOp.NotEq => !string.Equals(actual, a.Value, StringComparison.OrdinalIgnoreCase),
        CompareOp.In => a.Values.Contains(actual, StringComparer.OrdinalIgnoreCase),
        CompareOp.NotIn => !a.Values.Contains(actual, StringComparer.OrdinalIgnoreCase),
        _ => throw new NotSupportedException($"{a.Op} on a string"),
    };

    private static bool CompareDates(DateOnly actual, AttributeCompare a, DateOnly today) => a.Op switch
    {
        CompareOp.Before => actual < DateOnly.ParseExact(a.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        CompareOp.After => actual > DateOnly.ParseExact(a.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        CompareOp.WithinLastDays => actual >= today.AddDays(-int.Parse(a.Value, CultureInfo.InvariantCulture)),
        CompareOp.NotWithinLastDays => actual < today.AddDays(-int.Parse(a.Value, CultureInfo.InvariantCulture)),
        _ => throw new NotSupportedException($"{a.Op} on a date"),
    };

    private static bool CompareNumbers(double actual, CompareOp op, double expected) => op switch
    {
        CompareOp.Eq => actual == expected,
        CompareOp.NotEq => actual != expected,
        CompareOp.Gt => actual > expected,
        CompareOp.Gte => actual >= expected,
        CompareOp.Lt => actual < expected,
        CompareOp.Lte => actual <= expected,
        _ => throw new NotSupportedException($"{op} on a number"),
    };
}
