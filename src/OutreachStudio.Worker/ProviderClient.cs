using System.Net.Http.Json;

namespace OutreachStudio.Worker;

/// <summary>What a provider is asked to send. Mirrors the mock provider contract so the worker does not depend on it.</summary>
public sealed record SendRequest(long DeliveryId, string To, string Title, string Body);

/// <summary>What a provider answers with on success.</summary>
public sealed record SendResponse(string MessageId);

/// <summary>The outcome of one provider call. A failure carries the text the pipeline stores in last_error.</summary>
public sealed record ProviderResult(bool Succeeded, string? MessageId, string? Error)
{
    public static ProviderResult Ok(string? messageId) => new(true, messageId, null);

    public static ProviderResult Failed(string error) => new(false, null, error);
}

public interface IProviderClient
{
    Task<ProviderResult> SendAsync(string provider, SendRequest request, CancellationToken ct);
}

/// <summary>
/// Posts to the mock provider service. Non-2xx is a failure carrying the response body, because the
/// retry and failover decisions belong to the pipeline and not to an exception filter.
/// </summary>
public sealed class HttpProviderClient(HttpClient http) : IProviderClient
{
    public async Task<ProviderResult> SendAsync(string provider, SendRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync($"/{provider}/send", request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return ProviderResult.Failed($"{(int)response.StatusCode} {body}".Trim());
        }
        var payload = await response.Content.ReadFromJsonAsync<SendResponse>(ct);
        return ProviderResult.Ok(payload?.MessageId);
    }
}
