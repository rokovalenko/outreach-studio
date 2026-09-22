using OutreachStudio.Data;
using OutreachStudio.Engine.Audience;
using OutreachStudio.Web.Client.Api;

namespace OutreachStudio.Web.Services;

/// <summary>
/// One copy of the user base in memory, shared by the rule estimates, the schedule pass and the
/// /api/audience download. It is a singleton so it opens its own scope for a database context.
/// Suppressions are not part of it because they are checked at send time, not at estimate time.
/// </summary>
public sealed class AudienceCache(IServiceScopeFactory scopes, ILogger<AudienceCache> logger) : IAudienceSource
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<AudienceUser> users = [];
    private DateTimeOffset loadedAt;

    public async Task<IReadOnlyList<AudienceUser>> LoadAsync(CancellationToken ct = default)
    {
        if (Fresh)
        {
            return users;
        }
        await gate.WaitAsync(ct);
        try
        {
            if (Fresh)
            {
                return users;
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OutreachDbContext>();
            users = await AudienceSnapshot.LoadAsync(db, ct);
            loadedAt = DateTimeOffset.UtcNow;
            logger.LogInformation("Loaded {Count} users into the audience cache", users.Count);
            return users;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Drops the snapshot so the next read reloads it.</summary>
    public void Invalidate() => loadedAt = default;

    private bool Fresh => loadedAt != default && DateTimeOffset.UtcNow - loadedAt < MaxAge;
}
