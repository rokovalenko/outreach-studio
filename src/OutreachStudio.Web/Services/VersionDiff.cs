using System.Globalization;
using OutreachStudio.Data;
using OutreachStudio.Engine.Rules;

namespace OutreachStudio.Web.Services;

public sealed record DiffRow(string Field, string Left, string Right)
{
    public bool Changed => !string.Equals(Left, Right, StringComparison.Ordinal);
}

public sealed record DiffLine(string Left, string Right)
{
    public bool Changed => !string.Equals(Left, Right, StringComparison.Ordinal);
}

/// <summary>
/// Two campaign versions side by side. Fields are compared one by one and the rule is compared line
/// by line at the same position, which is enough because the rule JSON is written by one serialiser
/// with one property order.
/// </summary>
public static class VersionDiff
{
    public static IReadOnlyList<DiffRow> Fields(string name, CampaignVersion left, CampaignVersion right) =>
    [
        // The name lives on the campaign, not on the version, so it is the same on both sides.
        new("Name", name, name),
        new("Channels", RuleWords.Describe(left.Channels), RuleWords.Describe(right.Channels)),
        new("Push title", left.PushTitle, right.PushTitle),
        new("Push body", left.PushBody, right.PushBody),
        new("Email subject", left.EmailSubject, right.EmailSubject),
        new("Email body", left.EmailBody, right.EmailBody),
        new("Start", Format.Utc(left.StartAt), Format.Utc(right.StartAt)),
        new("Per minute", left.PerMinute.ToString(CultureInfo.InvariantCulture), right.PerMinute.ToString(CultureInfo.InvariantCulture)),
        new("Quiet hours", QuietHours(left), QuietHours(right)),
        new("Holdout", Format.Share(left.HoldoutShare), Format.Share(right.HoldoutShare)),
    ];

    public static IReadOnlyList<DiffLine> RuleLines(CampaignVersion left, CampaignVersion right)
    {
        var a = Lines(left.RuleJson);
        var b = Lines(right.RuleJson);
        return Enumerable.Range(0, Math.Max(a.Length, b.Length))
            .Select(i => new DiffLine(i < a.Length ? a[i] : "", i < b.Length ? b[i] : ""))
            .ToList();
    }

    /// <summary>Rule.JsonOptions writes indented, so the stored text is already the text we show.</summary>
    public static string Pretty(string ruleJson) => Rule.FromJson(ruleJson).ToJson();

    private static string[] Lines(string ruleJson) => Pretty(ruleJson).Split('\n');

    private static string QuietHours(CampaignVersion version) =>
        version.QuietStartHour == version.QuietEndHour
            ? "none"
            : $"{version.QuietStartHour:00}:00 to {version.QuietEndHour:00}:00 local";
}
