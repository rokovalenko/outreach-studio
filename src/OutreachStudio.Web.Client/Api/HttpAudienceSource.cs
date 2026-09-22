using System.Diagnostics;
using System.Net.Http.Json;
using OutreachStudio.Engine.Audience;

namespace OutreachStudio.Web.Client.Api;

/// <summary>
/// One download of the whole user base per browser session. The list is held for the life of the
/// app because every keystroke in the rule builder runs over it, and a second trip to the server
/// would defeat the point of evaluating in the browser.
/// </summary>
public sealed class HttpAudienceSource(HttpClient http) : IAudienceSource, IAudienceDownloadStats
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<AudienceUser>? _users;

    public long ElapsedMs { get; private set; }

    public long? Bytes { get; private set; }

    public async Task<IReadOnlyList<AudienceUser>> LoadAsync(CancellationToken ct = default)
    {
        if (_users is not null)
        {
            return _users;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_users is null)
            {
                var watch = Stopwatch.StartNew();
                using var response = await http.GetAsync("api/audience", ct);
                response.EnsureSuccessStatusCode();
                var bytes = response.Content.Headers.ContentLength;
                var users = await response.Content.ReadFromJsonAsync<List<AudienceUser>>(JsonDefaults.Web, ct);
                watch.Stop();
                ElapsedMs = watch.ElapsedMilliseconds;
                Bytes = bytes;
                _users = users ?? [];
            }
        }
        finally
        {
            _gate.Release();
        }

        return _users;
    }
}
