using OutreachStudio.Engine.Campaigns;

namespace OutreachStudio.Web.Services;

/// <summary>Channel flags in words. The rule itself is worded by the editor's RuleText so both sides read the same.</summary>
public static class RuleWords
{
    public static string Describe(Channels channels) => channels switch
    {
        Engine.Campaigns.Channels.Both => "Push and email",
        Engine.Campaigns.Channels.Push => "Push",
        Engine.Campaigns.Channels.Email => "Email",
        _ => "No channel",
    };
}
