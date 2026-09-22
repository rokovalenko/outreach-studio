using System.Globalization;
using OutreachStudio.Engine.Audience;
using OutreachStudio.Engine.Rules;

namespace OutreachStudio.Web.Client.Editor;

/// <summary>
/// The edits the tree builder makes. Rules are immutable records, so each of these returns a new
/// tree and the page stores it in place of the old one.
/// </summary>
public static class RuleEdit
{
    /// <summary>Splits a clause from the Not that may wrap it, so a row can show its own toggle.</summary>
    public static (Rule Inner, bool Negated) Unwrap(Rule rule) =>
        rule is NotRule not ? (not.Child, true) : (rule, false);

    public static Rule Wrap(Rule inner, bool negated) => negated ? new NotRule(inner) : inner;

    /// <summary>The audience tab edits a group at the root, so a lone clause is put inside one.</summary>
    public static Rule AsGroup(Rule rule) => rule is AndRule or OrRule ? rule : new AndRule([rule]);

    public static IReadOnlyList<Rule> ChildrenOf(Rule group) => group switch
    {
        AndRule and => and.Children,
        OrRule or => or.Children,
        _ => [],
    };

    public static Rule WithChildren(Rule group, IReadOnlyList<Rule> children) =>
        group is OrRule ? new OrRule(children) : new AndRule(children);

    /// <summary>A fresh condition starts on an enum attribute, so the row is valid the moment it appears.</summary>
    public static AttributeCompare NewCondition()
    {
        var def = UserSchema.Attributes.FirstOrDefault(a => a.Type == AttributeType.Enum)
                  ?? UserSchema.Attributes[0];
        var op = def.Ops[0];
        return new AttributeCompare(def.Name, op, DefaultValue(def, op));
    }

    public static EventCountInWindow NewEvent() => new(UserSchema.Events[0].Type, CompareOp.Gte, 1, 30);

    public static AndRule NewGroup() => new([]);

    /// <summary>A value that parses for the attribute's type and the chosen operator.</summary>
    public static string DefaultValue(AttributeDefinition def, CompareOp op) => def.Type switch
    {
        AttributeType.Enum => def.EnumValues.Count > 0 ? def.EnumValues[0] : "",
        AttributeType.Bool => "true",
        AttributeType.Number => "0",
        AttributeType.Date => IsDayCount(op)
            ? "30"
            : DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => "",
    };

    /// <summary>True when the operator reads the value as a number of days rather than a date.</summary>
    public static bool IsDayCount(CompareOp op) =>
        op is CompareOp.WithinLastDays or CompareOp.NotWithinLastDays;

    public static bool IsList(CompareOp op) => op is CompareOp.In or CompareOp.NotIn;

    /// <summary>Keeps the value usable when the operator changes what the value means.</summary>
    public static string Retarget(AttributeDefinition def, string value, CompareOp previousOp, CompareOp op)
    {
        if (def.Type == AttributeType.Date)
        {
            return IsDayCount(previousOp) == IsDayCount(op) ? value : DefaultValue(def, op);
        }

        if (IsList(previousOp) && !IsList(op))
        {
            var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : DefaultValue(def, op);
        }

        return value;
    }
}
