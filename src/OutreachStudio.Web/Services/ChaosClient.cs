namespace OutreachStudio.Web.Services;

/// <summary>
/// The same shape the providers service keeps per provider. It is declared again here instead of
/// referencing that project, because the web app is a client of it and not part of it.
/// </summary>
public sealed record ProviderChaos(
    string Provider,
    string Channel,
    int LatencyMs = 0,
    double ErrorRate = 0,
    bool Down = false,
    bool DuplicateReceipts = false);

/// <summary>Reads and writes the chaos settings on the providers service over service discovery.</summary>
public sealed class ChaosClient(IHttpClientFactory factory)
{
    public async Task<IReadOnlyList<ProviderChaos>> GetAsync(CancellationToken ct)
    {
        using var http = factory.CreateClient("providers");
        return await http.GetFromJsonAsync<List<ProviderChaos>>("/chaos", ct) ?? [];
    }

    public async Task<ProviderChaos?> SetAsync(ProviderChaos settings, CancellationToken ct)
    {
        using var http = factory.CreateClient("providers");
        var response = await http.PutAsJsonAsync($"/chaos/{settings.Provider}", settings, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<ProviderChaos>(ct);
    }
}
