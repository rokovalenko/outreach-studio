using System.Collections.Concurrent;

namespace OutreachStudio.Providers;

/// <summary>Per-provider failure settings, changed from the web app while a send is in flight.</summary>
public sealed record ProviderChaos(
    string Provider,
    string Channel,
    int LatencyMs = 0,
    double ErrorRate = 0,
    bool Down = false,
    bool DuplicateReceipts = false);

public sealed class ChaosState
{
    public static readonly string[] Providers = ["push-a", "push-b", "email-a", "email-b"];

    private readonly ConcurrentDictionary<string, ProviderChaos> settings = new(
        Providers.Select(p => new KeyValuePair<string, ProviderChaos>(p, new ProviderChaos(p, p.Split('-')[0]))));

    public IReadOnlyList<ProviderChaos> All => Providers.Select(p => settings[p]).ToList();

    public ProviderChaos? Get(string provider) => settings.GetValueOrDefault(provider);

    public bool Set(ProviderChaos chaos)
    {
        if (!settings.ContainsKey(chaos.Provider))
        {
            return false;
        }
        settings[chaos.Provider] = chaos with { Channel = chaos.Provider.Split('-')[0] };
        return true;
    }
}
