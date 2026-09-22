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

    /// <summary>Sends per bucket, for the curve. Buckets start at the schedule start and are <paramref name="bucketMinutes"/> wide.</summary>
    public static IReadOnlyList<(DateTimeOffset Start, int Count)> Histogram(IReadOnlyList<PlannedSend> plan, DateTimeOffset start, int bucketMinutes)
    {
        var buckets = new SortedDictionary<long, int>();
        foreach (var p in plan)
        {
            if (p.Holdout)
            {
                continue;
            }
            var minutes = (long)Math.Floor((p.DueAt - start).TotalMinutes / bucketMinutes) * bucketMinutes;
            buckets[minutes] = buckets.TryGetValue(minutes, out var n) ? n + 1 : 1;
        }
        return buckets.Select(b => (start.AddMinutes(b.Key), b.Value)).ToList();
    }
}
