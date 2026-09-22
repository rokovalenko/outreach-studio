using OutreachStudio.Engine.Audience;
using OutreachStudio.Engine.Rules;
using static OutreachStudio.Engine.Tests.Fixtures;

namespace OutreachStudio.Engine.Tests;

public class RuleEvaluatorTests
{
    [Theory]
    [InlineData("tier", CompareOp.Eq, "Gold", true)]
    [InlineData("tier", CompareOp.Eq, "gold", true)]
    [InlineData("tier", CompareOp.NotEq, "Gold", false)]
    [InlineData("tier", CompareOp.In, "Gold, Platinum", true)]
    [InlineData("tier", CompareOp.NotIn, "Gold,Platinum", false)]
    [InlineData("country", CompareOp.Eq, "PL", true)]
    [InlineData("points_balance", CompareOp.Gte, "1200", true)]
    [InlineData("points_balance", CompareOp.Gt, "1200", false)]
    [InlineData("points_balance", CompareOp.Lt, "1500", true)]
    [InlineData("marketing_consent", CompareOp.Eq, "true", true)]
    [InlineData("marketing_consent", CompareOp.Eq, "false", false)]
    [InlineData("platform", CompareOp.Eq, "Android", true)]
    public void Attribute_compare(string attribute, CompareOp op, string value, bool expected)
    {
        var rule = new AttributeCompare(attribute, op, value);
        Assert.Equal(expected, RuleEvaluator.Matches(rule, User(), Now));
    }

    [Theory]
    [InlineData(CompareOp.WithinLastDays, "7", true)]
    [InlineData(CompareOp.WithinLastDays, "2", false)]
    [InlineData(CompareOp.NotWithinLastDays, "2", true)]
    [InlineData(CompareOp.Before, "2026-09-20", true)]
    [InlineData(CompareOp.Before, "2026-09-19", false)]
    [InlineData(CompareOp.After, "2026-09-18", true)]
    public void Date_compare_on_last_active(CompareOp op, string value, bool expected)
    {
        var rule = new AttributeCompare("last_active", op, value);
        Assert.Equal(expected, RuleEvaluator.Matches(rule, User(lastActiveDaysAgo: 3), Now));
    }

    [Fact]
    public void Event_count_in_window_counts_only_inside_the_window()
    {
        var user = User(events: (EventType.RewardClaimed, [1, 10, 29, 31, 80]));

        Assert.True(RuleEvaluator.Matches(new EventCountInWindow(EventType.RewardClaimed, CompareOp.Eq, 3, 30), user, Now));
        Assert.True(RuleEvaluator.Matches(new EventCountInWindow(EventType.RewardClaimed, CompareOp.Gte, 5, 90), user, Now));
        Assert.False(RuleEvaluator.Matches(new EventCountInWindow(EventType.RewardClaimed, CompareOp.Gte, 2, 5), user, Now));
        Assert.True(RuleEvaluator.Matches(new EventCountInWindow(EventType.Purchase, CompareOp.Eq, 0, 30), user, Now));
    }

    [Fact]
    public void And_or_not_compose()
    {
        var user = User(tier: Tier.Silver, country: "DE");
        var rule = new AndRule([
            new OrRule([new AttributeCompare("country", CompareOp.Eq, "PL"), new AttributeCompare("country", CompareOp.Eq, "DE")]),
            new NotRule(new AttributeCompare("tier", CompareOp.Eq, "Bronze")),
        ]);
        Assert.True(RuleEvaluator.Matches(rule, user, Now));
        Assert.False(RuleEvaluator.Matches(rule, User(tier: Tier.Bronze, country: "DE"), Now));
    }

    [Fact]
    public void Empty_and_matches_everyone_and_empty_or_matches_nobody()
    {
        Assert.True(RuleEvaluator.Matches(Rule.Everyone(), User(), Now));
        Assert.False(RuleEvaluator.Matches(new OrRule([]), User(), Now));
    }

    [Fact]
    public void Explain_reports_each_clause_with_its_actual_value()
    {
        var user = User(points: 900, events: (EventType.Login, [1, 2, 3]));
        var rule = new AndRule([
            new AttributeCompare("points_balance", CompareOp.Gte, "1000"),
            new EventCountInWindow(EventType.Login, CompareOp.Gte, 2, 7),
        ]);

        var result = RuleEvaluator.Explain(rule, user, Now);

        Assert.False(result.Matched);
        Assert.Equal("1 of 2", result.Actual);
        Assert.False(result.Children[0].Matched);
        Assert.Equal("900", result.Children[0].Actual);
        Assert.True(result.Children[1].Matched);
        Assert.Equal("3", result.Children[1].Actual);
    }

    [Fact]
    public void Explain_and_matches_agree()
    {
        var users = Enumerable.Range(1, 50).Select(i => User(id: i, points: i * 100, tier: (Tier)(i % 4), events: (EventType.Purchase, [i % 40]))).ToList();
        var rule = new OrRule([
            new AndRule([new AttributeCompare("tier", CompareOp.In, "Gold,Platinum"), new AttributeCompare("points_balance", CompareOp.Lt, "2500")]),
            new EventCountInWindow(EventType.Purchase, CompareOp.Gte, 1, 7),
        ]);
        foreach (var u in users)
        {
            Assert.Equal(RuleEvaluator.Matches(rule, u, Now), RuleEvaluator.Explain(rule, u, Now).Matched);
        }
    }

    [Fact]
    public void Estimate_counts_samples_and_breaks_down()
    {
        var users = Enumerable.Range(1, 100).Select(i => User(id: i, country: i % 2 == 0 ? "PL" : "DE", tier: i % 5 == 0 ? Tier.Platinum : Tier.Bronze)).ToList();
        var estimate = AudienceEstimate.Compute(new AttributeCompare("country", CompareOp.Eq, "PL"), users, Now, sampleSize: 10);

        Assert.Equal(100, estimate.Total);
        Assert.Equal(50, estimate.Matched);
        Assert.Equal(0.5, estimate.Share);
        Assert.Equal(10, estimate.Sample.Count);
        Assert.Equal(50, estimate.ByCountry["PL"]);
        Assert.Equal(10, estimate.ByTier["Platinum"]);
        Assert.Equal(40, estimate.ByTier["Bronze"]);
    }
}
