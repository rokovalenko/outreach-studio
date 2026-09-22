using OutreachStudio.Engine.Audience;
using OutreachStudio.Engine.Rules;

namespace OutreachStudio.Engine.Tests;

public class RuleJsonAndValidationTests
{
    [Fact]
    public void Round_trips_through_json_with_kind_discriminators()
    {
        var rule = new AndRule([
            new AttributeCompare("tier", CompareOp.In, "Gold,Platinum"),
            new NotRule(new EventCountInWindow(EventType.RewardClaimed, CompareOp.Gte, 2, 30)),
            new OrRule([]),
        ]);

        var json = rule.ToJson();
        var back = Rule.FromJson(json);

        Assert.Contains("\"kind\": \"and\"", json, StringComparison.Ordinal);
        Assert.Contains("\"event\": \"RewardClaimed\"", json, StringComparison.Ordinal);
        Assert.Equal(json, back.ToJson());
        Assert.IsType<NotRule>(((AndRule)back).Children[1]);
    }

    [Fact]
    public void Valid_rule_has_no_errors()
    {
        var rule = new AndRule([
            new AttributeCompare("country", CompareOp.In, "PL,DE"),
            new AttributeCompare("signup_date", CompareOp.WithinLastDays, "90"),
            new AttributeCompare("last_active", CompareOp.Before, "2026-01-01"),
            new AttributeCompare("points_balance", CompareOp.Gte, "2000"),
            new AttributeCompare("marketing_consent", CompareOp.Eq, "true"),
            new EventCountInWindow(EventType.Login, CompareOp.Gte, 1, 7),
        ]);
        Assert.Empty(RuleValidator.Validate(rule));
    }

    [Theory]
    [InlineData("colour", CompareOp.Eq, "red", "Unknown attribute")]
    [InlineData("tier", CompareOp.Gt, "Gold", "does not apply")]
    [InlineData("tier", CompareOp.Eq, "Diamond", "not a valid enum")]
    [InlineData("points_balance", CompareOp.Gte, "lots", "not a valid number")]
    [InlineData("signup_date", CompareOp.Before, "yesterday", "not a valid date")]
    [InlineData("signup_date", CompareOp.WithinLastDays, "0", "not a valid date")]
    [InlineData("marketing_consent", CompareOp.Eq, "yes", "not a valid bool")]
    [InlineData("country", CompareOp.In, "", "value is empty")]
    public void Invalid_attribute_clauses_are_reported(string attribute, CompareOp op, string value, string expectedFragment)
    {
        var errors = RuleValidator.Validate(new AttributeCompare(attribute, op, value));
        Assert.Single(errors);
        Assert.Contains(expectedFragment, errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_event_clauses_are_reported()
    {
        var errors = RuleValidator.Validate(new AndRule([
            new EventCountInWindow(EventType.Login, CompareOp.In, 1, 7),
            new EventCountInWindow(EventType.Login, CompareOp.Gte, -1, 0),
        ]));
        Assert.Equal(3, errors.Count);
    }
}
