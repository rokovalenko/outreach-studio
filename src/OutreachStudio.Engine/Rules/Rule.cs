using System.Text.Json;
using System.Text.Json.Serialization;
using OutreachStudio.Engine.Audience;

namespace OutreachStudio.Engine.Rules;

public enum CompareOp
{
    Eq, NotEq, Gt, Gte, Lt, Lte, In, NotIn, Before, After, WithinLastDays, NotWithinLastDays,
}

/// <summary>
/// The rule tree. The JSON on the wire and the object in memory are the same definition, so a
/// campaign version stores exactly what the browser evaluated. Values are strings and the
/// attribute's type in <see cref="UserSchema"/> says how to parse them.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AndRule), "and")]
[JsonDerivedType(typeof(OrRule), "or")]
[JsonDerivedType(typeof(NotRule), "not")]
[JsonDerivedType(typeof(AttributeCompare), "attribute")]
[JsonDerivedType(typeof(EventCountInWindow), "event")]
public abstract record Rule
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static Rule FromJson(string json) =>
        JsonSerializer.Deserialize<Rule>(json, JsonOptions) ?? throw new JsonException("Rule JSON is null");

    public static AndRule Everyone() => new([]);
}

public sealed record AndRule(IReadOnlyList<Rule> Children) : Rule;

public sealed record OrRule(IReadOnlyList<Rule> Children) : Rule;

public sealed record NotRule(Rule Child) : Rule;

/// <summary>Compare one attribute with a value. For In and NotIn the value is comma separated.</summary>
public sealed record AttributeCompare(string Attribute, CompareOp Op, string Value) : Rule
{
    public IReadOnlyList<string> Values => Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>"Reward claimed at least 2 times in the last 30 days".</summary>
public sealed record EventCountInWindow(EventType Event, CompareOp Op, int Count, int WindowDays) : Rule;
