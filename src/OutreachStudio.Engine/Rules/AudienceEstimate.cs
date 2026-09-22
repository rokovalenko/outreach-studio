using OutreachStudio.Engine.Audience;

namespace OutreachStudio.Engine.Rules;

/// <summary>
/// One pass over the user base: how many match, a sample of them and the breakdowns the builder
/// shows. The browser runs this on every debounced edit and the server runs it once at schedule time.
/// </summary>
public sealed record AudienceEstimate(
    int Total,
    int Matched,
    IReadOnlyList<AudienceUser> Sample,
    IReadOnlyDictionary<string, int> ByCountry,
    IReadOnlyDictionary<string, int> ByTier,
    IReadOnlyDictionary<string, int> ByPlatform)
{
    public double Share => Total == 0 ? 0 : (double)Matched / Total;

    public static AudienceEstimate Compute(Rule rule, IReadOnlyList<AudienceUser> users, DateTimeOffset now, int sampleSize = 10)
    {
        var matched = 0;
        var sample = new List<AudienceUser>(sampleSize);
        var byCountry = new Dictionary<string, int>(StringComparer.Ordinal);
        var byTier = new Dictionary<string, int>(StringComparer.Ordinal);
        var byPlatform = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var user in users)
        {
            if (!RuleEvaluator.Matches(rule, user, now))
            {
                continue;
            }
            matched++;
            if (sample.Count < sampleSize)
            {
                sample.Add(user);
            }
            Bump(byCountry, user.Country);
            Bump(byTier, user.Tier.ToString());
            Bump(byPlatform, user.Platform.ToString());
        }

        return new(users.Count, matched, sample, byCountry, byTier, byPlatform);
    }

    public static IEnumerable<AudienceUser> Select(Rule rule, IEnumerable<AudienceUser> users, DateTimeOffset now) =>
        users.Where(u => RuleEvaluator.Matches(rule, u, now));

    private static void Bump(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
}
