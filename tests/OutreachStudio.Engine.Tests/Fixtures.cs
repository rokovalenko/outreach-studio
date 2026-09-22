using OutreachStudio.Engine.Audience;

namespace OutreachStudio.Engine.Tests;

internal static class Fixtures
{
    public static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    public static AudienceUser User(
        int id = 1,
        string country = "PL",
        Tier tier = Tier.Gold,
        Platform platform = Platform.Android,
        bool consent = true,
        int points = 1200,
        string timeZone = "Europe/Warsaw",
        int signupDaysAgo = 400,
        int lastActiveDaysAgo = 3,
        params (EventType Type, int[] DaysAgo)[] events)
    {
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        var grouped = events.ToDictionary(
            e => e.Type,
            e => e.DaysAgo.Select(d => Now.AddDays(-d).ToUnixTimeSeconds()).Order().ToArray());
        return new AudienceUser(id, "Kara", $"kara.{id}@example.net", country, tier, today.AddDays(-signupDaysAgo),
            today.AddDays(-lastActiveDaysAgo), platform, consent, timeZone, points, grouped);
    }
}
