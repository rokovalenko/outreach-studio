using OutreachStudio.Data;
using OutreachStudio.Data.Seeding;
using OutreachStudio.Data.Synthetic;
using OutreachStudio.Engine.Messaging;
using OutreachStudio.Engine.Rules;
using static OutreachStudio.Engine.Tests.Fixtures;

namespace OutreachStudio.Engine.Tests;

public class SyntheticDataTests
{
    [Fact]
    public void Same_seed_gives_the_same_users()
    {
        var a = SyntheticUsers.Generate(Now, count: 500);
        var b = SyntheticUsers.Generate(Now, count: 500);
        Assert.Equal(a.Select(u => (u.Email, u.Tier, u.LastActive)), b.Select(u => (u.Email, u.Tier, u.LastActive)));
        Assert.Equal(a.Select(u => u.Events.Sum(e => e.Value.Sum())), b.Select(u => u.Events.Sum(e => e.Value.Sum())));
        Assert.Equal(500, a.Select(u => u.Email).Distinct().Count());
    }

    [Fact]
    public void Base_has_a_believable_shape()
    {
        var users = SyntheticUsers.Generate(Now);
        var events = users.Sum(u => u.Events.Values.Sum(v => v.Length));

        Assert.Equal(SyntheticUsers.DefaultCount, users.Count);
        Assert.InRange(events / (double)users.Count, 10, 40);
        Assert.InRange(users.Count(u => u.MarketingConsent) / (double)users.Count, 0.75, 0.9);
        Assert.InRange(users.Count(u => u.Country == "PL") / (double)users.Count, 0.12, 0.25);
        Assert.True(users.All(u => u.Events.Values.All(v => v.SequenceEqual(v.Order()))), "event timestamps are sorted");
    }

    [Fact]
    public void Every_seeded_campaign_is_valid_and_reaches_a_real_audience()
    {
        var users = SyntheticUsers.Generate(Now);
        foreach (var seed in SeedCampaigns.All)
        {
            Assert.Empty(RuleValidator.Validate(seed.Rule));
            Assert.Empty(Personalisation.Validate(new MessageTemplate(seed.PushTitle, seed.PushBody, seed.EmailSubject, seed.EmailBody)));

            var estimate = AudienceEstimate.Compute(seed.Rule, users, Now);
            var expectedMin = seed.Status is CampaignStatus.Draft ? 0 : 150;
            Assert.True(estimate.Matched >= expectedMin, $"{seed.Name} reaches {estimate.Matched} users");
            Assert.True(estimate.Matched < users.Count || seed.Rule is AndRule { Children.Count: 0 }, $"{seed.Name} reaches everyone");
        }
    }
}
