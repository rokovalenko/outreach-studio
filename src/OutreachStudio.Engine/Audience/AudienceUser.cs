namespace OutreachStudio.Engine.Audience;

public enum Tier { Bronze, Silver, Gold, Platinum }

public enum Platform { iOS, Android, Web }

public enum EventType { PointsEarned, RewardClaimed, MissionCompleted, Purchase, Login, CampaignOpened }

/// <summary>
/// The view of a user that rules run against. The same shape is stored in Postgres, shipped to the
/// browser for the live estimate and read by the worker at send time. Event timestamps are unix
/// seconds sorted ascending per type, so a count in a window is two binary searches.
/// </summary>
public sealed record AudienceUser(
    int Id,
    string FirstName,
    string Email,
    string Country,
    Tier Tier,
    DateOnly SignupDate,
    DateOnly LastActive,
    Platform Platform,
    bool MarketingConsent,
    string TimeZone,
    int PointsBalance,
    IReadOnlyDictionary<EventType, long[]> Events)
{
    public int CountEvents(EventType type, DateTimeOffset from, DateTimeOffset to)
    {
        if (!Events.TryGetValue(type, out var stamps) || stamps.Length == 0)
        {
            return 0;
        }
        var lo = LowerBound(stamps, from.ToUnixTimeSeconds());
        var hi = LowerBound(stamps, to.ToUnixTimeSeconds() + 1);
        return hi - lo;
    }

    private static int LowerBound(long[] sorted, long value)
    {
        var index = Array.BinarySearch(sorted, value);
        return index >= 0 ? index : ~index;
    }
}
