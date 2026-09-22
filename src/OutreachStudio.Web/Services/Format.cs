using System.Globalization;

namespace OutreachStudio.Web.Services;

/// <summary>
/// Times are shown in UTC with the suffix, because the pages render on the server and the server
/// does not know the reader's zone. Quiet hours are the one place a local time matters and that is
/// handled per user at plan time.
/// </summary>
public static class Format
{
    public static string Utc(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    public static string Utc(DateTimeOffset? at) => at is null ? "" : Utc(at.Value);

    public static string TimeUtc(DateTimeOffset at) =>
        at.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Share(double share) => (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    /// <summary>An enum name as a phrase, so ConsentRevoked reads as Consent revoked.</summary>
    public static string Words(string name)
    {
        var text = string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));
        return text;
    }

    public static string Countdown(TimeSpan left) => left <= TimeSpan.Zero
        ? "now"
        : left.TotalHours >= 1
            ? $"{(int)left.TotalHours} h {left.Minutes} min"
            : left.TotalMinutes >= 1
                ? $"{left.Minutes} min {left.Seconds} s"
                : $"{left.Seconds} s";
}
