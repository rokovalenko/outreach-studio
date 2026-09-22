using Microsoft.EntityFrameworkCore;
using OutreachStudio.Data;

namespace OutreachStudio.Worker.Tests;

/// <summary>The send policy: three tries on the primary, one on the secondary, then the dead letter.</summary>
[Collection(PostgresCollection.Name)]
public sealed class FailoverTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Primary_down_sends_through_the_secondary_after_three_tries()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = new Harness(fixture);
        harness.Providers.AlwaysFails("push-a");
        harness.Providers.Succeeds("push-b");
        var seeded = await Build.OneAsync(fixture, ct);

        await harness.DrainAsync(ct);

        await using var db = fixture.NewContext();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == seeded.DeliveryId, ct);
        Assert.Equal(DeliveryStatus.Sent, delivery.Status);
        Assert.Equal("push-b", delivery.Provider);
        Assert.Equal(1, delivery.Attempts);
        Assert.NotNull(delivery.ProviderMessageId);

        var attempts = await db.DeliveryAttempts.AsNoTracking().Where(a => a.DeliveryId == seeded.DeliveryId).ToListAsync(ct);
        Assert.Equal(4, attempts.Count);
        Assert.Equal(3, attempts.Count(a => a.Provider == "push-a" && !a.Succeeded));
        Assert.Single(attempts, a => a.Provider == "push-b" && a.Succeeded);
    }

    [Fact]
    public async Task Both_providers_down_dead_letters_after_the_attempt_limit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = new Harness(fixture);
        harness.Providers.AlwaysFails("push-a");
        harness.Providers.AlwaysFails("push-b");
        var seeded = await Build.OneAsync(fixture, ct);

        // Each pass is one round of four provider calls, the backoff between rounds is brought forward.
        for (var round = 0; round < 3; round++)
        {
            await harness.DrainAsync(ct);
            await harness.MakeDueNowAsync(seeded.CampaignId, ct);
        }

        await using var db = fixture.NewContext();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == seeded.DeliveryId, ct);
        Assert.Equal(DeliveryStatus.Failed, delivery.Status);
        Assert.Equal(3, delivery.Attempts);
        Assert.Contains("rejected", delivery.LastError);
        Assert.Equal(12, await db.DeliveryAttempts.CountAsync(a => a.DeliveryId == seeded.DeliveryId, ct));
    }
}
