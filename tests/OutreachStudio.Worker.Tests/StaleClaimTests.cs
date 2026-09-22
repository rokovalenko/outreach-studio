using Microsoft.EntityFrameworkCore;
using OutreachStudio.Data;

namespace OutreachStudio.Worker.Tests;

[Collection(PostgresCollection.Name)]
public sealed class StaleClaimTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_claim_left_by_a_dead_worker_is_taken_over_and_sent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = new Harness(fixture);
        harness.Providers.Succeeds("push-a");
        var seeded = await Build.OneAsync(fixture, ct);

        await using (var db = fixture.NewContext())
        {
            var delivery = await db.Deliveries.FirstAsync(d => d.Id == seeded.DeliveryId, ct);
            delivery.Status = DeliveryStatus.Claimed;
            delivery.ClaimedBy = "worker-that-died";
            delivery.ClaimedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
            await db.SaveChangesAsync(ct);
        }

        await harness.DrainAsync(ct);

        await using var check = fixture.NewContext();
        var sent = await check.Deliveries.AsNoTracking().FirstAsync(d => d.Id == seeded.DeliveryId, ct);
        Assert.Equal(DeliveryStatus.Sent, sent.Status);
        Assert.Equal("push-a", sent.Provider);
        Assert.NotEqual("worker-that-died", sent.ClaimedBy);
    }
}
