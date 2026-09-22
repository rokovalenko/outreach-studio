using System.Threading.Channels;

namespace OutreachStudio.Providers;

/// <summary>
/// Plays the part of the provider's delivery pipeline: a delivered receipt a few seconds after the
/// send, an opened receipt for some of them later. Receipts go to the web app's receipt endpoint.
/// With duplicate receipts on, delivered is posted twice, so the receiver has to be idempotent.
/// </summary>
public sealed class ReceiptSimulator(IHttpClientFactory httpFactory, ChaosState chaos, ILogger<ReceiptSimulator> logger) : BackgroundService
{
    private readonly Channel<(string Provider, string MessageId)> accepted = Channel.CreateUnbounded<(string, string)>();

    public void Accepted(string provider, string messageId) => accepted.Writer.TryWrite((provider, messageId));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var (provider, messageId) in accepted.Reader.ReadAllAsync(ct))
        {
            _ = Simulate(provider, messageId, ct);
        }
    }

    private async Task Simulate(string provider, string messageId, CancellationToken ct)
    {
        var rng = Random.Shared;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1 + rng.NextDouble() * 6), ct);
            if (rng.NextDouble() < 0.04)
            {
                return; // Lost in transit. The delivery stays Sent, which is what happens with real providers.
            }
            await Post(new Receipt(messageId, Receipt.Delivered, DateTimeOffset.UtcNow), ct);
            if (chaos.Get(provider)?.DuplicateReceipts == true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 + rng.Next(800)), ct);
                await Post(new Receipt(messageId, Receipt.Delivered, DateTimeOffset.UtcNow), ct);
            }
            if (rng.NextDouble() < (provider.StartsWith("push", StringComparison.Ordinal) ? 0.38 : 0.27))
            {
                await Task.Delay(TimeSpan.FromSeconds(3 + rng.NextDouble() * 20), ct);
                await Post(new Receipt(messageId, Receipt.Opened, DateTimeOffset.UtcNow), ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Receipt for {MessageId} was not delivered", messageId);
        }
    }

    private async Task Post(Receipt receipt, CancellationToken ct)
    {
        using var http = httpFactory.CreateClient("web");
        using var response = await http.PostAsJsonAsync("/api/receipts", receipt, ct);
        response.EnsureSuccessStatusCode();
    }
}
