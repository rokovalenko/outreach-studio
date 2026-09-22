using System.Globalization;
using OutreachStudio.Engine.Audience;

namespace OutreachStudio.Engine.Rules;

/// <summary>
/// Checks a rule tree against the schema before it is evaluated or saved: unknown attribute, an
/// operator the attribute's type does not support, or a value that does not parse as that type.
/// </summary>
public static class RuleValidator
{
    public static IReadOnlyList<string> Validate(Rule rule)
    {
        var errors = new List<string>();
        Walk(rule, errors);
        return errors;
    }

    private static void Walk(Rule rule, List<string> errors)
    {
        switch (rule)
        {
            case AndRule and:
                foreach (var c in and.Children) { Walk(c, errors); }
                break;
            case OrRule or:
                foreach (var c in or.Children) { Walk(c, errors); }
                break;
            case NotRule not:
                Walk(not.Child, errors);
                break;
            case EventCountInWindow e:
                if (e.Count < 0) { errors.Add($"{e.Event}: count must be zero or more"); }
                if (e.WindowDays <= 0) { errors.Add($"{e.Event}: window must be at least one day"); }
                if (e.Op is not (CompareOp.Eq or CompareOp.NotEq or CompareOp.Gt or CompareOp.Gte or CompareOp.Lt or CompareOp.Lte))
                {
                    errors.Add($"{e.Event}: {e.Op} is not a count comparison");
                }
                break;
            case AttributeCompare a:
                ValidateAttribute(a, errors);
                break;
        }
    }

    private static void ValidateAttribute(AttributeCompare a, List<string> errors)
    {
        var def = UserSchema.Find(a.Attribute);
        if (def is null)
        {
            errors.Add($"Unknown attribute '{a.Attribute}'");
            return;
        }
        if (!def.Ops.Contains(a.Op))
        {
            errors.Add($"{def.Label}: {a.Op} does not apply to a {def.Type.ToString().ToLowerInvariant()}");
            return;
        }
        var values = a.Op is CompareOp.In or CompareOp.NotIn ? a.Values : [a.Value];
        if (values.Count == 0)
        {
            errors.Add($"{def.Label}: value is empty");
            return;
        }
        foreach (var v in values)
        {
            if (!Parses(def, a.Op, v))
            {
                errors.Add($"{def.Label}: '{v}' is not a valid {def.Type.ToString().ToLowerInvariant()}");
            }
        }
    }

    private static bool Parses(AttributeDefinition def, CompareOp op, string value) => def.Type switch
    {
        AttributeType.String => value.Length > 0,
        AttributeType.Enum => def.EnumValues.Contains(value, StringComparer.OrdinalIgnoreCase),
        AttributeType.Number => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
        AttributeType.Bool => bool.TryParse(value, out _),
        AttributeType.Date => op is CompareOp.WithinLastDays or CompareOp.NotWithinLastDays
            ? int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days > 0
            : DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        _ => false,
    };
}
