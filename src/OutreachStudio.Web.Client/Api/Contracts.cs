using OutreachStudio.Engine.Audience;
using OutreachStudio.Engine.Campaigns;
using OutreachStudio.Engine.Messaging;
using OutreachStudio.Engine.Scheduling;

namespace OutreachStudio.Web.Client.Api;

/// <summary>
/// The campaign editor renders with interactive auto: first over the server circuit, then in the
/// browser once the runtime is downloaded. Both hosts register these two interfaces, the server
/// against the database and the browser against the JSON endpoints, so the component never knows
/// where it runs.
/// </summary>
public interface ICampaignEditorApi
{
    Task<CampaignEditorModel> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Writes a new immutable version when anything changed. Returns the current version number.</summary>
    Task<int> SaveAsync(Guid id, CampaignEdit edit, CancellationToken ct = default);

    /// <summary>Draft to InReview.</summary>
    Task SubmitAsync(Guid id, CancellationToken ct = default);
}

public interface IAudienceSource
{
    /// <summary>The whole user base, cached. In the browser this is one download per session.</summary>
    Task<IReadOnlyList<AudienceUser>> LoadAsync(CancellationToken ct = default);
}

/// <summary>BlastRadiusShare is the share of the base above which the warning shows and two approvals are needed.</summary>
public sealed record CampaignEditorModel(
    Guid Id,
    string Name,
    CampaignStatus Status,
    int Version,
    CampaignEdit Current,
    double BlastRadiusShare)
{
    public bool Editable => Status is CampaignStatus.Draft;
}

public sealed record CampaignEdit(
    string Name,
    string RuleJson,
    Channels Channels,
    MessageTemplate Message,
    Schedule Schedule);
