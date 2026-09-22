using System.Collections.Concurrent;

namespace OutreachStudio.Worker.Tests;

/// <summary>
/// Stands in for the provider service. It records every call and refuses to be entered twice for
/// the same delivery at the same time, which is the evidence that two workers never share a row.
/// </summary>
public sealed class FakeProviderClient : IProviderClient
{
    private readonly ConcurrentQueue<Call> _calls = new();
    private readonly ConcurrentDictionary<string, Func<bool>> _behaviours = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, int> _inFlight = new();

    public IReadOnlyList<Call> Calls => [.. _calls];

    /// <summary>Set when one delivery was inside the client twice at once.</summary>
    public bool SawOverlap { get; private set; }

    public void Succeeds(string provider) => _behaviours[provider] = () => true;

    public void AlwaysFails(string provider) => _behaviours[provider] = () => false;

    public void FailsAtRandom(string provider, double rate) => _behaviours[provider] = () => Random.Shared.NextDouble() >= rate;

    public async Task<ProviderResult> SendAsync(string provider, SendRequest request, CancellationToken ct)
    {
        if (_inFlight.AddOrUpdate(request.DeliveryId, 1, (_, n) => n + 1) > 1)
        {
            SawOverlap = true;
        }
        // A short wait so two workers on the same row would actually overlap here.
        await Task.Delay(1, ct);
        var succeeded = _behaviours.TryGetValue(provider, out var behaviour) ? behaviour() : true;
        _calls.Enqueue(new Call(provider, request.DeliveryId, succeeded, DateTimeOffset.UtcNow));
        _inFlight.AddOrUpdate(request.DeliveryId, 0, (_, n) => n - 1);
        return succeeded
            ? ProviderResult.Ok($"{provider}-{request.DeliveryId}-{_calls.Count}")
            : ProviderResult.Failed($"{provider} rejected the message");
    }

    public sealed record Call(string Provider, long DeliveryId, bool Succeeded, DateTimeOffset At);
}
