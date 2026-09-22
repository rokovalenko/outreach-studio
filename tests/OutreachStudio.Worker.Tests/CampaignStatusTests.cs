using Microsoft.EntityFrameworkCore;
using OutreachStudio.Data;
using OutreachStudio.Engine.Campaigns;

namespace OutreachStudio.Worker.Tests;

[Collection(PostgresCollection.Name)]
public sealed class CampaignStatusTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Campaign_moves_to_sending_on_the_first_row_and_to_done_when_drained()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = new Harness(fixture, configure: o => o.BatchSize = 1);
        harness.Providers.Succeeds("push-a");

        Guid campaignId;
        await using (var db = fixture.NewContext())
        {
            var campaign = Build.Campaign();
            var users = new[] { Build.User(), Build.User() };
            db.Campaigns.Add(campaign);
            db.Users.AddRange(users);
            await db.SaveChangesAsync(ct);
            db.Deliveries.AddRange(users.Select(u => Build.Delivery(campaign, u)));
            await db.SaveChangesAsync(ct);
            campaignId = campaign.Id;
        }

        Assert.Equal(1, await harness.NewWorker().RunCycleAsync(ct));
        Assert.Equal(CampaignStatus.Sending, await StatusAsync(campaignId, ct));

        await harness.DrainAsync(ct);

        await using var check = fixture.NewContext();
        var campaign2 = await check.Campaigns.AsNoTracking().FirstAsync(c => c.Id == campaignId, ct);
        Assert.Equal(CampaignStatus.Done, campaign2.Status);
        Assert.NotNull(campaign2.FinishedAt);
    }

    [Fact]
    public async Task Campaign_fails_when_every_row_dead_letters()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = new Harness(fixture, configure: o => o.MaxAttempts = 1);
        harness.Providers.AlwaysFails("push-a");
        harness.Providers.AlwaysFails("push-b");
        var seeded = await Build.OneAsync(fixture, ct);

        await harness.DrainAsync(ct);

        await using var db = fixture.NewContext();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == seeded.DeliveryId, ct);
        Assert.Equal(DeliveryStatus.Failed, delivery.Status);
        Assert.Equal(CampaignStatus.Failed, await StatusAsync(seeded.CampaignId, ct));
    }

    private async Task<CampaignStatus> StatusAsync(Guid campaignId, CancellationToken ct)
    {
        await using var db = fixture.NewContext();
        var campaign = await db.Campaigns.AsNoTracking().FirstAsync(c => c.Id == campaignId, ct);
        return campaign.Status;
    }
}
