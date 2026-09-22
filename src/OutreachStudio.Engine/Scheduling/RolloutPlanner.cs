using OutreachStudio.Engine.Audience;

namespace OutreachStudio.Engine.Scheduling;

public sealed record PlannedSend(int UserId, DateTimeOffset DueAt, bool Holdout);

/// <summary>
/// Turns an audience and a schedule into a due time per user. The browser draws the result as the
/// rollout curve before send, the server writes the same result into the outbox at schedule time.
/// Users are spread evenly at the throttle rate and then moved out of their quiet hours.
/// </summary>
public static class RolloutPlanner
{
    public static IReadOnlyList<PlannedSend> Plan(Guid campaignId, IEnumerable<AudienceUser> audience, Schedule schedule)
    {
        var perSecond = Math.Max(schedule.PerMinute, 1) / 60.0;
        var index = 0;
        var plan = new List<PlannedSend>();
        foreach (var user in audience)
        {
            var holdout = Holdout.Contains(campaignId, user.Id, schedule.HoldoutShare);
            var slot = schedule.StartAt.AddSeconds(index / perSecond);
            var due = holdout ? slot : QuietHours.NextAllowed(slot, user.TimeZone, schedule);
            plan.Add(new(user.Id, due, holdout));
            index++;
        }
        return plan;
    }

    /// <summary>
    /// Sends per bucket, for the curve. Buckets are <paramref name="bucketMinutes"/> wide, start at the
    /// schedule start and run without gaps to the last send, so a quiet night shows as empty bars.
    /// </summary>
    public static IReadOnlyList<(DateTimeOffset Start, int Count)> Histogram(IReadOnlyList<PlannedSend> plan, DateTimeOffset start, int bucketMinutes)
    {
        var counts = new Dictionary<long, int>();
        long last = 0;
        foreach (var p in plan)
        {
            if (p.Holdout)
            {
                continue;
            }
            var bucket = (long)Math.Floor((p.DueAt - start).TotalMinutes / bucketMinutes);
            counts[bucket] = counts.TryGetValue(bucket, out var n) ? n + 1 : 1;
            last = Math.Max(last, bucket);
        }
        if (counts.Count == 0)
        {
            return [];
        }
        return Enumerable.Range(0, (int)last + 1)
            .Select(i => (start.AddMinutes((long)i * bucketMinutes), counts.GetValueOrDefault(i)))
            .ToList();
    }
}
