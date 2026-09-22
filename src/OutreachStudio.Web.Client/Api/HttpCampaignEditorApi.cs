using System.Net.Http.Json;

namespace OutreachStudio.Web.Client.Api;

/// <summary>
/// The browser half of the editor API. The server registers its own implementation of the same
/// interface against the database, so the page never learns which one it is talking to.
/// </summary>
public sealed class HttpCampaignEditorApi(HttpClient http) : ICampaignEditorApi
{
    public async Task<CampaignEditorModel> GetAsync(Guid id, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<CampaignEditorModel>($"api/campaigns/{id}", JsonDefaults.Web, ct)
        ?? throw new InvalidOperationException($"Campaign {id} was not found");

    public async Task<int> SaveAsync(Guid id, CampaignEdit edit, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"api/campaigns/{id}", edit, JsonDefaults.Web, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<int>(JsonDefaults.Web, ct);
    }

    public async Task SubmitAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"api/campaigns/{id}/submit", content: null, ct);
        response.EnsureSuccessStatusCode();
    }
}
