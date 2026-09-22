using System.Text.RegularExpressions;
using OutreachStudio.Engine.Audience;

namespace OutreachStudio.Engine.Messaging;

public enum Channel { Push, Email }

/// <summary>Both channel variants of a campaign's message. Text may contain {{token}} placeholders.</summary>
public sealed record MessageTemplate(string PushTitle, string PushBody, string EmailSubject, string EmailBody)
{
    public static readonly MessageTemplate Empty = new("", "", "", "");
}

public sealed record RenderedMessage(string Title, string Body);

/// <summary>
/// Personalisation tokens are checked against <see cref="UserSchema.Tokens"/> when the author types,
/// so an unknown token is a validation error and never a blank in a sent message.
/// </summary>
public static partial class Personalisation
{
    [GeneratedRegex(@"\{\{\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}")]
    private static partial Regex TokenPattern();

    public static IReadOnlyList<string> Validate(MessageTemplate template)
    {
        var errors = new List<string>();
        Check("Push title", template.PushTitle, errors);
        Check("Push body", template.PushBody, errors);
        Check("Email subject", template.EmailSubject, errors);
        Check("Email body", template.EmailBody, errors);
        return errors;
    }

    public static RenderedMessage Render(MessageTemplate template, Channel channel, AudienceUser user) => channel switch
    {
        Channel.Push => new(Fill(template.PushTitle, user), Fill(template.PushBody, user)),
        Channel.Email => new(Fill(template.EmailSubject, user), Fill(template.EmailBody, user)),
        _ => throw new NotSupportedException(channel.ToString()),
    };

    public static string Fill(string text, AudienceUser user) =>
        TokenPattern().Replace(text, m => UserSchema.Tokens.TryGetValue(m.Groups[1].Value, out var read)
            ? read(user)
            : throw new InvalidOperationException($"Unknown token '{m.Groups[1].Value}'"));

    private static void Check(string field, string text, List<string> errors)
    {
        foreach (Match m in TokenPattern().Matches(text))
        {
            var token = m.Groups[1].Value;
            if (!UserSchema.Tokens.ContainsKey(token))
            {
                errors.Add($"{field}: unknown token '{token}'. Known tokens: {string.Join(", ", UserSchema.Tokens.Keys)}");
            }
        }
    }
}
