using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using OutreachStudio.Engine.Audience;
using OutreachStudio.Engine.Rules;
using OutreachStudio.Web.Client.Api;

namespace OutreachStudio.E2E;

[Collection("stack")]
public sealed partial class CampaignJourneyTests(StackFixture stack)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [GeneratedRegex(@"([\d,]+) of ([\d,]+) users")]
    private static partial Regex CountText();

    [Fact]
    public async Task Dashboard_shows_the_seeded_history()
    {
        var page = await stack.NewPageAsync();
        await page.GotoAsync(stack.WebUrl + "/");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Row)).ToHaveCountAsync(13, new() { Timeout = 30_000 });
        await Expect(page.GetByText("Weekly mission reminder")).ToBeVisibleAsync();
        await Expect(page.GetByText("Lapsed Bronze win-back")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Browser_count_matches_the_server_for_the_same_rule()
    {
        var id = await CampaignIdAsync("Brazil catalogue launch");
        var model = await stack.Web.GetFromJsonAsync<CampaignEditorModel>($"/api/campaigns/{id}", Json, Ct);
        Assert.NotNull(model);

        // The editor evaluates the rule in WebAssembly. The test evaluates it here, on the server
        // runtime, over the same snapshot the browser downloads. ADR-001 rests on these agreeing.
        await using var snapshot = await stack.Web.GetStreamAsync("/api/audience", Ct);
        var users = AudienceSnapshotFormat.Read(snapshot);
        var expected = AudienceEstimate.Compute(Rule.FromJson(model.Current.RuleJson), users, DateTimeOffset.UtcNow).Matched;

        var page = await stack.NewPageAsync();
        // The first visit renders over the server circuit while the runtime downloads. Once the
        // runtime is cached the next visit runs in the browser, which is the path this test is about.
        var runtime = page.WaitForResponseAsync(r => r.Url.Contains("dotnet.native", StringComparison.Ordinal) && r.Url.EndsWith(".wasm", StringComparison.Ordinal), new() { Timeout = 90_000 });
        await page.GotoAsync($"{stack.WebUrl}/campaigns/{id}/edit");
        // The first editor render after a fresh seed loads the user base into the server cache.
        await Expect(page.GetByText("of 20,000 users")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await runtime;
        await page.GotoAsync($"{stack.WebUrl}/campaigns/{id}/edit");
        await Expect(page.GetByText("evaluated in your browser")).ToBeVisibleAsync(new() { Timeout = 90_000 });

        var heading = await page.GetByText("of 20,000 users").InnerTextAsync();
        var shown = int.Parse(CountText().Match(heading).Groups[1].Value.Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, shown);
        Assert.InRange(shown, 1, 19_999);
    }

    [Fact]
    public async Task A_draft_is_edited_approved_scheduled_and_sends()
    {
        var id = await CampaignIdAsync("iOS survey");
        var page = await stack.NewPageAsync();

        await page.GotoAsync($"{stack.WebUrl}/campaigns/{id}/edit");
        await Expect(page.GetByText("of 20,000 users")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(page.GetByText("Saved as version")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit for approval" }).First.ClickAsync();

        await Expect(page).ToHaveURLAsync($"{stack.WebUrl}/campaigns/{id}");
        await Expect(page.GetByText("In review")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Schedule now" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Schedule now" }).ClickAsync();

        await Expect(page).ToHaveURLAsync($"{stack.WebUrl}/campaigns/{id}/live");
        await Expect(page.GetByText("Sending", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 60_000 });
        // The about popover in the app bar also names push-a, hidden until opened.
        await Expect(page.GetByText("push-a").Filter(new() { Visible = true }).First).ToBeVisibleAsync();
        await Expect(page.Locator("text=/user \\d+/").First).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await page.GotoAsync($"{stack.WebUrl}/campaigns/{id}/results");
        await Expect(page.GetByText("Funnel")).ToBeVisibleAsync();
        await Expect(page.GetByText("Skipped by reason")).ToBeVisibleAsync();
    }

    private async Task<Guid> CampaignIdAsync(string name)
    {
        var page = await stack.NewPageAsync();
        await page.GotoAsync(stack.WebUrl + "/");
        await page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@"/campaigns/[0-9a-f-]{36}$"));
        var url = page.Url;
        await page.CloseAsync();
        return Guid.Parse(url[^36..]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);
}
