using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OutreachStudio.Data;

namespace OutreachStudio.Worker.Tests;

/// <summary>
/// The worker's own registrations minus the host: the real pipeline and the real claim SQL against
/// the container, with the provider and the clock swapped.
/// </summary>
public sealed class Harness : IAsyncDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly ServiceProvider _services;

    public Harness(PostgresFixture fixture, TimeProvider? clock = null, Action<OutboxOptions>? configure = null)
    {
        _fixture = fixture;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutreachDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddSingleton<IProviderClient>(Providers);
        services.Configure<OutboxOptions>(o => configure?.Invoke(o));
        services.AddScoped<DeliveryPipeline>();
        _services = services.BuildServiceProvider();
    }

    public FakeProviderClient Providers { get; } = new();

    public OutboxWorker NewWorker() => new(
        _services.GetRequiredService<IServiceScopeFactory>(),
        _services.GetRequiredService<IOptions<OutboxOptions>>(),
        _services.GetRequiredService<TimeProvider>(),
        _services.GetRequiredService<ILogger<OutboxWorker>>());

    /// <summary>Runs the given number of workers until each of them finds nothing left to claim.</summary>
    public async Task DrainAsync(CancellationToken ct, int workers = 1)
    {
        var running = Enumerable.Range(0, workers).Select(_ => DrainOneAsync(NewWorker(), ct)).ToList();
        await Task.WhenAll(running);
    }

    /// <summary>Brings retries forward so a backoff does not have to be waited out.</summary>
    public async Task MakeDueNowAsync(Guid campaignId, CancellationToken ct)
    {
        await using var db = _fixture.NewContext();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE deliveries SET due_at = now() WHERE campaign_id = {campaignId} AND status = 'Queued'", ct);
    }

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    private static async Task DrainOneAsync(OutboxWorker worker, CancellationToken ct)
    {
        while (await worker.RunCycleAsync(ct) > 0)
        {
        }
    }
}

/// <summary>A clock that does not move, for the quiet hours test.</summary>
public sealed class FrozenClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
}
