namespace OutreachStudio.Engine.Scheduling;

/// <summary>
/// When and how fast a campaign goes out. Quiet hours are in each user's own time zone and the
/// send is pushed to the next allowed local time, never skipped.
/// </summary>
public sealed record Schedule(
    DateTimeOffset StartAt,
    int PerMinute,
    int QuietStartHour,
    int QuietEndHour,
    double HoldoutShare)
{
    public static Schedule Default(DateTimeOffset startAt) => new(startAt, 600, 22, 8, 0.05);

    public bool HasQuietHours => QuietStartHour != QuietEndHour;
}

public static class QuietHours
{
    /// <summary>The first moment at or after <paramref name="at"/> that is outside quiet hours in <paramref name="timeZone"/>.</summary>
    public static DateTimeOffset NextAllowed(DateTimeOffset at, string timeZone, Schedule schedule)
    {
        if (!schedule.HasQuietHours)
        {
            return at;
        }
        var tz = FindTimeZone(timeZone);
        var local = TimeZoneInfo.ConvertTime(at, tz);
        if (!IsQuiet(local.Hour, schedule))
        {
            return at;
        }
        var wake = local.Date.AddHours(schedule.QuietEndHour);
        if (wake <= local.DateTime)
        {
            wake = wake.AddDays(1);
        }
        var offset = tz.GetUtcOffset(wake);
        return new DateTimeOffset(wake, offset).ToUniversalTime();
    }

    public static bool IsQuiet(int hour, Schedule s) => s.QuietStartHour < s.QuietEndHour
        ? hour >= s.QuietStartHour && hour < s.QuietEndHour
        : hour >= s.QuietStartHour || hour < s.QuietEndHour;

    private static TimeZoneInfo FindTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

/// <summary>
/// A user is in the holdout when a hash of the campaign and user ids lands below the share, so
/// the group is stable across replans, workers and reruns without storing a flag per user.
/// </summary>
public static class Holdout
{
    public static bool Contains(Guid campaignId, int userId, double share)
    {
        if (share <= 0)
        {
            return false;
        }
        var h = 2166136261u;
        foreach (var b in campaignId.ToByteArray())
        {
            h = (h ^ b) * 16777619u;
        }
        h = (h ^ (uint)userId) * 16777619u;
        h ^= h >> 13;
        h *= 0x5bd1e995u;
        h ^= h >> 15;
        return h / (double)uint.MaxValue < share;
    }
}
