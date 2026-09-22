using Microsoft.EntityFrameworkCore;
using OutreachStudio.Data;
using OutreachStudio.Engine.Messaging;

namespace OutreachStudio.Worker.Tests;

/// <summary>
/// The guardrails run at send time, so everything here changes the world after the rows are already
/// in the outbox and still expects the worker to hold the line.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GuardrailTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Consent_revoked_after_enqueue_skips_without_a_provider_call()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = new Harness(fixture);
        harness.Providers.Succeeds("push-a");
        var seeded = await Build.OneAsync(fixture, ct);

        await using (var db = fixture.NewContext())
        {
            var user = await db.Users.FirstAsync(u => u.Id == seeded.UserId, ct);
            user.MarketingConsent = false;
            await db.SaveChangesAsync(ct);
        }

        await harness.DrainAsync(ct);

        var delivery = await ReadAsync(seeded.DeliveryId, ct);
        Assert.Equal(DeliveryStatus.Skipped, delivery.Status);
        Assert.Equal(SkipReason.ConsentRevoked, delivery.SkipReason);
        Assert.DoesNotContain(harness.Providers.Calls, c => c.DeliveryId == seeded.DeliveryId);
    }

    [Fact]
    public async Task Suppressed_email_skips_without_a_provider_call()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = new Harness(fixture);
        harness.Providers.Succeeds("email-a");
        var seeded = await Build.OneAsync(fixture, ct, channel: Channel.Email);

        await using (var db = fixture.NewContext())
        {
            var user = await db.Users.FirstAsync(u => u.Id == seeded.UserId, ct);
            db.Suppressions.Add(new Suppression { Email = user.Email, Reason = "Unsubscribed", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }

        await harness.DrainAsync(ct);

        var delivery = await ReadAsync(seeded.DeliveryId, ct);
        Assert.Equal(DeliveryStatus.Skipped, delivery.Status);
        Assert.Equal(SkipReason.Suppressed, delivery.SkipReason);
        Assert.DoesNotContain(harness.Providers.Calls, c => c.DeliveryId == seeded.DeliveryId);
    }

    [Fact]
    public async Task Frequency_cap_counts_only_sends_inside_the_window()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = new Harness(fixture);
        harness.Providers.Succeeds("push-a");

        long cappedDeliveryId;
        long freeDeliveryId;
        await using (var db = fixture.NewContext())
        {
            var campaign = Build.Campaign();
            var capped = Build.User();
            var free = Build.User();
            db.Campaigns.Add(campaign);
            db.Users.AddRange(capped, free);
            await db.SaveChangesAsync(ct);

            // Three earlier campaigns, recent for one user and long past for the other.
            var now = DateTimeOffset.UtcNow;
            foreach (var index in Enumerable.Range(0, 3))
            {
                var earlier = Build.Campaign();
                db.Campaigns.Add(earlier);
                await db.SaveChangesAsync(ct);
                db.Deliveries.Add(Sent(earlier, capped, now.AddDays(-1).AddMinutes(-index)));
                db.Deliveries.Add(Sent(earlier, free, now.AddDays(-30).AddMinutes(-index)));
            }
            var cappedDelivery = Build.Delivery(campaign, capped);
            var freeDelivery = Build.Delivery(campaign, free);
            db.Deliveries.AddRange(cappedDelivery, freeDelivery);
            await db.SaveChangesAsync(ct);

            cappedDeliveryId = cappedDelivery.Id;
            freeDeliveryId = freeDelivery.Id;
        }

        await harness.DrainAsync(ct);

        var capped2 = await ReadAsync(cappedDeliveryId, ct);
        Assert.Equal(DeliveryStatus.Skipped, capped2.Status);
        Assert.Equal(SkipReason.FrequencyCap, capped2.SkipReason);

        var free2 = await ReadAsync(freeDeliveryId, ct);
        Assert.Equal(DeliveryStatus.Sent, free2.Status);
        Assert.DoesNotContain(harness.Providers.Calls, c => c.DeliveryId == cappedDeliveryId);
        Assert.Contains(harness.Providers.Calls, c => c.DeliveryId == freeDeliveryId && c.Succeeded);
    }

    [Fact]
    public async Task Quiet_hours_push_the_row_back_to_the_queue_at_the_local_wake_time()
    {
        var ct = TestContext.Current.CancellationToken;
        // 23:00 in Warsaw, inside the default 22 to 8 quiet window. The date is ahead of the other
        // tests so the row this one parks is never due for their drains.
        var frozen = new DateTimeOffset(2027, 3, 10, 23, 0, 0, TimeSpan.FromHours(1));
        await using var harness = new Harness(fixture, new FrozenClock(frozen));
        harness.Providers.Succeeds("push-a");
        var seeded = await Build.OneAsync(fixture, ct, timeZone: "Europe/Warsaw");

        await harness.NewWorker().RunCycleAsync(ct);

        var delivery = await ReadAsync(seeded.DeliveryId, ct);
        var expected = new DateTimeOffset(2027, 3, 11, 8, 0, 0, TimeSpan.FromHours(1));
        Assert.Equal(DeliveryStatus.Queued, delivery.Status);
        Assert.Equal(expected.ToUniversalTime(), delivery.DueAt.ToUniversalTime());
        Assert.Null(delivery.ClaimedBy);
        Assert.DoesNotContain(harness.Providers.Calls, c => c.DeliveryId == seeded.DeliveryId);
    }

    private static Delivery Sent(Campaign campaign, User user, DateTimeOffset sentAt) => new()
    {
        CampaignId = campaign.Id,
        UserId = user.Id,
        Channel = Channel.Push,
        Status = DeliveryStatus.Sent,
        DueAt = sentAt,
        SentAt = sentAt,
        Attempts = 1,
        Provider = "push-a",
    };

    private async Task<Delivery> ReadAsync(long deliveryId, CancellationToken ct)
    {
        await using var db = fixture.NewContext();
        return await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == deliveryId, ct);
    }
}
