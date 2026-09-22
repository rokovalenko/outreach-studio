using System.Text.Json;
using Npgsql;

namespace OutreachStudio.Web.Services;

/// <summary>
/// The payload the deliveries_notify trigger puts on the delivery_events channel. At is when this
/// process read it, because the trigger does not send a timestamp.
/// </summary>
public sealed record DeliveryEvent(
    Guid CampaignId,
    long DeliveryId,
    int UserId,
    string Channel,
    string Status,
    string? SkipReason,
    string? Provider,
    int Attempts,
    string? Error)
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// One connection that does nothing but LISTEN delivery_events and raise <see cref="Received"/>.
/// The live board subscribes to it instead of polling the deliveries table. The last events per
/// campaign are kept so a board opened in the middle of a send starts with something on screen.
/// </summary>
public sealed class DeliveryFeed(IConfiguration configuration, ILogger<DeliveryFeed> logger) : BackgroundService
{
    private const int RingSize = 50;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private readonly Lock ring = new();
    private readonly Dictionary<Guid, Queue<DeliveryEvent>> recent = [];

    /// <summary>Raised on the notification thread, so handlers marshal to their own context.</summary>
    public event Action<DeliveryEvent>? Received;

    /// <summary>The last events seen for one campaign, oldest first.</summary>
    public IReadOnlyList<DeliveryEvent> Recent(Guid campaignId)
    {
        lock (ring)
        {
            return recent.TryGetValue(campaignId, out var queue) ? [.. queue] : [];
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration.GetConnectionString("outreach");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                connection.Notification += OnNotification;
                await connection.OpenAsync(stoppingToken);
                await using (var listen = new NpgsqlCommand("LISTEN delivery_events", connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }
                logger.LogInformation("Listening on delivery_events");
                while (!stoppingToken.IsCancellationRequested)
                {
                    await connection.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The database restarts, the connection is cut, or the payload is not what we expect.
                // Wait a moment and open a new connection, because a dead listener shows a dead board.
                logger.LogWarning(ex, "Lost the delivery_events listener, reconnecting in {Seconds} s", RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private void OnNotification(object sender, NpgsqlNotificationEventArgs args)
    {
        DeliveryEvent? received;
        try
        {
            received = JsonSerializer.Deserialize<DeliveryEvent>(args.Payload, PayloadJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not read a delivery_events payload");
            return;
        }
        if (received is null)
        {
            return;
        }

        lock (ring)
        {
            if (!recent.TryGetValue(received.CampaignId, out var queue))
            {
                recent[received.CampaignId] = queue = new Queue<DeliveryEvent>(RingSize);
            }
            queue.Enqueue(received);
            while (queue.Count > RingSize)
            {
                queue.Dequeue();
            }
        }

        try
        {
            Received?.Invoke(received);
        }
        catch (Exception ex)
        {
            // A page that failed to handle an event must not take the listener down with it.
            logger.LogWarning(ex, "A delivery_events subscriber threw");
        }
    }
}
