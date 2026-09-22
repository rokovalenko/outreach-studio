using OutreachStudio.Engine.Messaging;
using static OutreachStudio.Engine.Tests.Fixtures;

namespace OutreachStudio.Engine.Tests;

public class PersonalisationTests
{
    [Fact]
    public void Renders_known_tokens_for_the_channel()
    {
        var template = new MessageTemplate("Hi {{first_name}}", "You have {{ points_balance }} points as {{tier}}", "Subject {{country}}", "Body");
        var user = User(points: 1200);

        var push = Personalisation.Render(template, Channel.Push, user);
        var email = Personalisation.Render(template, Channel.Email, user);

        Assert.Equal("Hi Kara", push.Title);
        Assert.Equal("You have 1200 points as Gold", push.Body);
        Assert.Equal("Subject PL", email.Title);
        Assert.Equal("Body", email.Body);
    }

    [Fact]
    public void Unknown_token_fails_validation_and_never_renders_blank()
    {
        var template = new MessageTemplate("Hi {{first_name}}", "Your {{nickname}}", "", "{{last_name}}");

        var errors = Personalisation.Validate(template);

        Assert.Equal(2, errors.Count);
        Assert.Contains("nickname", errors[0], StringComparison.Ordinal);
        Assert.Contains("last_name", errors[1], StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => Personalisation.Render(template, Channel.Push, User()));
    }

    [Fact]
    public void Text_without_tokens_is_valid_and_unchanged()
    {
        Assert.Empty(Personalisation.Validate(MessageTemplate.Empty));
        Assert.Equal("plain {not a token}", Personalisation.Fill("plain {not a token}", User()));
    }
}
