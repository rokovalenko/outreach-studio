using Microsoft.EntityFrameworkCore;
using OutreachStudio.Engine.Audience;

namespace OutreachStudio.Data;

/// <summary>
/// Loads the whole user base with event timestamps grouped per user and type, the shape the rule
/// engine wants. The web app caches one of these for the browser download and for scheduling.
/// </summary>
public static class AudienceSnapshot
{
    public static async Task<IReadOnlyList<AudienceUser>> LoadAsync(OutreachDbContext db, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(ct);
        var events = await db.UserEvents.AsNoTracking()
            .OrderBy(e => e.UserId).ThenBy(e => e.Type).ThenBy(e => e.OccurredAt)
            .Select(e => new { e.UserId, e.Type, Stamp = e.OccurredAt.ToUnixTimeSeconds() })
            .ToListAsync(ct);

        var byUser = new Dictionary<int, Dictionary<EventType, List<long>>>(users.Count);
        foreach (var e in events)
        {
            if (!byUser.TryGetValue(e.UserId, out var perType))
            {
                byUser[e.UserId] = perType = [];
            }
            if (!perType.TryGetValue(e.Type, out var list))
            {
                perType[e.Type] = list = [];
            }
            list.Add(e.Stamp);
        }

        var result = new List<AudienceUser>(users.Count);
        foreach (var u in users)
        {
            IReadOnlyDictionary<EventType, long[]> grouped = byUser.TryGetValue(u.Id, out var perType)
                ? perType.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray())
                : new Dictionary<EventType, long[]>();
            result.Add(u.ToAudienceUser(grouped));
        }
        return result;
    }
}
