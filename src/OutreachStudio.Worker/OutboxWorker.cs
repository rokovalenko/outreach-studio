using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using OutreachStudio.Data;
using OutreachStudio.Engine.Campaigns;

namespace OutreachStudio.Worker;

/// <summary>
/// Drains the deliveries outbox. Several replicas run the same loop and SKIP LOCKED decides who
/// gets which rows, so adding a replica adds throughput and never a duplicate send.
/// </summary>
public sealed class OutboxWorker(
    IServiceScopeFactory scopes,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    /// <summary>
    /// One statement claims the batch and hands back the rows. The lock is taken in the subquery so
    /// a second worker walks past the locked ids instead of waiting behind them.
    /// </summary>
    public const string ClaimSql = """
        UPDATE deliveries
        SET status = 'Claimed', claimed_by = @worker, claimed_at = now()
        WHERE id IN (
            SELECT id FROM deliveries
            WHERE status = 'Queued' AND due_at <= now()
            ORDER BY due_at
            LIMIT @n
            FOR UPDATE SKIP LOCKED)
        RETURNING *
        """;

    private static readonly TimeSpan ReclaimEvery = TimeSpan.FromSeconds(30);

    private DateTimeOffset _reclaimDue = DateTimeOffset.MinValue;

    /// <summary>Written into claimed_by so a stuck row says which replica was holding it.</summary>
    public string Name { get; } = $"{Environment.MachineName}-{Random.Shared.Next(0x1000, 0x10000):x}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox worker {Worker} started", Name);
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox cycle failed");
            }

            if (processed == 0)
            {
                try
                {
                    await Task.Delay(options.Value.IdleDelay, clock, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Reclaim, claim, process, then settle the campaigns the batch touched. Returns the batch size.
    /// Tests drive this directly so a drain has an end instead of a timeout.
    /// </summary>
    public async Task<int> RunCycleAsync(CancellationToken ct)
    {
        var settings = options.Value;
        List<Delivery> batch;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutreachDbContext>();
            await ReclaimStaleAsync(db, settings, ct);
            batch = await db.Deliveries
                .FromSqlRaw(ClaimSql, new NpgsqlParameter("worker", Name), new NpgsqlParameter("n", settings.BatchSize))
                .AsNoTracking()
                .ToListAsync(ct);
        }

        if (batch.Count == 0)
        {
            return 0;
        }
        Telemetry.Claimed.Add(batch.Count);

        var parallel = new ParallelOptions { MaxDegreeOfParallelism = settings.Parallelism, CancellationToken = ct };
        await Parallel.ForEachAsync(batch, parallel, async (delivery, token) =>
        {
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<DeliveryPipeline>().ProcessAsync(delivery, token);
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                // The row stays Claimed and the stale sweep hands it back, so one bad row is not a lost batch.
                logger.LogError(ex, "Delivery {DeliveryId} could not be processed", delivery.Id);
            }
        });

        await SettleCampaignsAsync(batch.Select(d => d.CampaignId).Distinct().ToList(), ct);
        return batch.Count;
    }

    /// <summary>A worker that died holds its claims forever, so old claims go back on the queue.</summary>
    private async Task ReclaimStaleAsync(OutreachDbContext db, OutboxOptions settings, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (now < _reclaimDue)
        {
            return;
        }
        _reclaimDue = now + ReclaimEvery;

        var cutoff = now - settings.ClaimTimeout;
        var rows = await db.Database.ExecuteSqlAsync(
            $"UPDATE deliveries SET status = 'Queued', claimed_by = NULL, claimed_at = NULL WHERE status = 'Claimed' AND claimed_at < {cutoff}",
            ct);
        if (rows > 0)
        {
            logger.LogWarning("Reclaimed {Rows} stale claims older than {Timeout}", rows, settings.ClaimTimeout);
        }
    }

    /// <summary>
    /// Campaign status follows the rows: first processed row moves it to Sending, an empty queue
    /// finishes it. Several workers may run this at once and each writes the same answer.
    /// </summary>
    private async Task SettleCampaignsAsync(IReadOnlyList<Guid> campaignIds, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutreachDbContext>();
        var now = clock.GetUtcNow();

        foreach (var id in campaignIds)
        {
            var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (campaign is null)
            {
                continue;
            }
            if (campaign.Status == CampaignStatus.Scheduled)
            {
                campaign.Status = CampaignStatus.Sending;
            }

            var pending = await db.Deliveries.CountAsync(
                d => d.CampaignId == id && (d.Status == DeliveryStatus.Queued || d.Status == DeliveryStatus.Claimed), ct);
            if (pending == 0)
            {
                var sent = await db.Deliveries.CountAsync(d => d.CampaignId == id && d.SentAt != null, ct);
                var failed = await db.Deliveries.CountAsync(d => d.CampaignId == id && d.Status == DeliveryStatus.Failed, ct);
                campaign.Status = sent == 0 && failed > 0 ? CampaignStatus.Failed : CampaignStatus.Done;
                campaign.FinishedAt = now;
            }
            campaign.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
