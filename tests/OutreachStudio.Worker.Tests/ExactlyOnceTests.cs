using Microsoft.EntityFrameworkCore;
using OutreachStudio.Data;
using OutreachStudio.Engine.Campaigns;

namespace OutreachStudio.Worker.Tests;

/// <summary>
/// The claim is the whole point of the outbox, so this is the test that has to hold: six workers,
/// one campaign, a primary that fails one call in ten, and still one send per user.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExactlyOnceTests(PostgresFixture fixture)
{
    private const int Audience = 3000;

    [Fact]
    public async Task Six_workers_send_every_delivery_exactly_once()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = new Harness(fixture);
        harness.Providers.FailsAtRandom("push-a", 0.10);
        harness.Providers.Succeeds("push-b");

        Guid campaignId;
        await using (var db = fixture.NewContext())
        {
            var campaign = Build.Campaign();
            db.Campaigns.Add(campaign);
            var users = Enumerable.Range(0, Audience).Select(_ => Build.User()).ToList();
            db.Users.AddRange(users);
            await db.SaveChangesAsync(ct);
            db.Deliveries.AddRange(users.Select(u => Build.Delivery(campaign, u)));
            await db.SaveChangesAsync(ct);
            campaignId = campaign.Id;
        }

        await harness.DrainAsync(ct, workers: 6);

        await using var check = fixture.NewContext();
        var statuses = await check.Deliveries.AsNoTracking()
            .Where(d => d.CampaignId == campaignId)
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        Assert.Equal([(DeliveryStatus.Sent, Audience)], statuses.Select(s => (s.Status, s.Count)));

        var successes = await check.DeliveryAttempts.AsNoTracking()
            .Where(a => check.Deliveries.Any(d => d.Id == a.DeliveryId && d.CampaignId == campaignId) && a.Succeeded)
            .GroupBy(a => a.DeliveryId)
            .Select(g => g.Count())
            .ToListAsync(ct);
        Assert.Equal(Audience, successes.Count);
        Assert.All(successes, count => Assert.Equal(1, count));

        var ids = await check.Deliveries.AsNoTracking().Where(d => d.CampaignId == campaignId).Select(d => d.Id).ToListAsync(ct);
        var mine = ids.ToHashSet();
        var calls = harness.Providers.Calls.Where(c => mine.Contains(c.DeliveryId)).ToList();
        Assert.Equal(Audience, calls.Count(c => c.Succeeded));
        Assert.True(calls.Count > Audience, "the injected primary failures never fired");
        Assert.False(harness.Providers.SawOverlap, "two workers held the same delivery at the same time");

        var campaign2 = await check.Campaigns.AsNoTracking().FirstAsync(c => c.Id == campaignId, ct);
        Assert.Equal(CampaignStatus.Done, campaign2.Status);
    }
}
