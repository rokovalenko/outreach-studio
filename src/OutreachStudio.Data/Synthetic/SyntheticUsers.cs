using OutreachStudio.Engine.Audience;

namespace OutreachStudio.Data.Synthetic;

/// <summary>
/// A deterministic user base. The same seed gives the same users, events and names on every
/// machine, so a rule in the README produces the count the README says. Nothing here comes from
/// a real data set: names are syllables, emails are on example.net.
/// </summary>
public static class SyntheticUsers
{
    public const int DefaultSeed = 7;
    public const int DefaultCount = 20_000;

    private static readonly string[] FirstSyllables = ["Ka", "Mi", "Lo", "Ta", "Ri", "Sa", "No", "Ve", "Ju", "El", "An", "Or", "Le", "Da", "Ni", "Ro"];
    private static readonly string[] SecondSyllables = ["ra", "len", "mi", "to", "na", "vi", "sha", "ko", "dan", "lin", "ya", "ris", "mo", "sel"];

    private static readonly (string Country, string[] TimeZones, int Weight)[] Countries =
    [
        ("PL", ["Europe/Warsaw"], 18),
        ("DE", ["Europe/Berlin"], 14),
        ("GB", ["Europe/London"], 12),
        ("ES", ["Europe/Madrid"], 8),
        ("IT", ["Europe/Rome"], 6),
        ("FR", ["Europe/Paris"], 6),
        ("NL", ["Europe/Amsterdam"], 4),
        ("SE", ["Europe/Stockholm"], 3),
        ("PT", ["Europe/Lisbon"], 3),
        ("US", ["America/New_York", "America/Chicago", "America/Los_Angeles"], 10),
        ("CA", ["America/Toronto"], 3),
        ("BR", ["America/Sao_Paulo"], 5),
        ("MX", ["America/Mexico_City"], 2),
        ("AU", ["Australia/Sydney"], 3),
        ("JP", ["Asia/Tokyo"], 2),
        ("IN", ["Asia/Kolkata"], 1),
    ];

    // Events per 30 days for a user with activity 1.0. Scaled by the user's activity level.
    private static readonly (EventType Type, double PerMonth)[] EventRates =
    [
        (EventType.Login, 3.0),
        (EventType.PointsEarned, 2.0),
        (EventType.MissionCompleted, 0.7),
        (EventType.RewardClaimed, 0.5),
        (EventType.Purchase, 0.3),
        (EventType.CampaignOpened, 0.6),
    ];

    private const int HistoryDays = 90;

    public static IReadOnlyList<AudienceUser> Generate(DateTimeOffset now, int seed = DefaultSeed, int count = DefaultCount)
    {
        var rng = new Random(seed);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var users = new List<AudienceUser>(count);
        var totalWeight = Countries.Sum(c => c.Weight);

        for (var id = 1; id <= count; id++)
        {
            var (country, zones, _) = PickWeighted(rng, totalWeight);
            var timeZone = zones[rng.Next(zones.Length)];
            var name = FirstSyllables[rng.Next(FirstSyllables.Length)] + SecondSyllables[rng.Next(SecondSyllables.Length)];
            var signup = today.AddDays(-rng.Next(7, 1100));
            var tier = PickTier(rng, signup, today);
            // Activity is log-normal-ish: most users quiet, a tail very active. Higher tiers skew up.
            var activity = Math.Exp(rng.NextDouble() * 2.2 - 1.4) * (1 + (int)tier * 0.35);
            // About a fifth of the base has lapsed: their events stop somewhere in the last three months.
            var activeUntil = rng.NextDouble() < 0.2 ? now.AddDays(-rng.Next(15, 85)) : now;
            var events = GenerateEvents(rng, activeUntil, activity, signup);
            var lastEvent = events.Values.Where(v => v.Length > 0).Select(v => v[^1]).DefaultIfEmpty(0).Max();
            var lastActive = lastEvent == 0 ? signup : DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(lastEvent).UtcDateTime);
            var platform = rng.NextDouble() switch { < 0.45 => Platform.Android, < 0.8 => Platform.iOS, _ => Platform.Web };
            var consent = rng.NextDouble() < 0.82;
            var points = (int)(rng.NextDouble() * rng.NextDouble() * 6000 * (1 + (int)tier));

            users.Add(new AudienceUser(id, name, $"{name.ToLowerInvariant()}.{id}@example.net", country, tier, signup, lastActive,
                platform, consent, timeZone, points, events));
        }
        return users;
    }

    private static (string, string[], int) PickWeighted(Random rng, int totalWeight)
    {
        var roll = rng.Next(totalWeight);
        foreach (var c in Countries)
        {
            roll -= c.Weight;
            if (roll < 0)
            {
                return c;
            }
        }
        return Countries[0];
    }

    private static Tier PickTier(Random rng, DateOnly signup, DateOnly today)
    {
        var tenureYears = (today.DayNumber - signup.DayNumber) / 365.0;
        var roll = rng.NextDouble() + tenureYears * 0.15;
        return roll switch { < 0.55 => Tier.Bronze, < 0.85 => Tier.Silver, < 0.97 => Tier.Gold, _ => Tier.Platinum };
    }

    private static Dictionary<EventType, long[]> GenerateEvents(Random rng, DateTimeOffset now, double activity, DateOnly signup)
    {
        var windowStart = now.AddDays(-HistoryDays);
        var signupAt = new DateTimeOffset(signup.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (signupAt > windowStart)
        {
            windowStart = signupAt;
        }
        var windowSeconds = (long)(now - windowStart).TotalSeconds;
        var months = windowSeconds / (30.0 * 86400);
        var result = new Dictionary<EventType, long[]>();
        foreach (var (type, perMonth) in EventRates)
        {
            var n = Poisson(rng, perMonth * activity * months);
            if (n == 0)
            {
                continue;
            }
            var stamps = new long[n];
            for (var i = 0; i < n; i++)
            {
                stamps[i] = windowStart.ToUnixTimeSeconds() + (long)(rng.NextDouble() * windowSeconds);
            }
            Array.Sort(stamps);
            result[type] = stamps;
        }
        return result;
    }

    private static int Poisson(Random rng, double lambda)
    {
        if (lambda <= 0)
        {
            return 0;
        }
        var l = Math.Exp(-lambda);
        var k = 0;
        var p = 1.0;
        do
        {
            k++;
            p *= rng.NextDouble();
        } while (p > l && k < 200);
        return k - 1;
    }
}
