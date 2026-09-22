using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;

namespace OutreachStudio.E2E;

/// <summary>
/// One AppHost (Postgres, web, workers, providers) and one headless Chromium for the whole run.
/// The first start seeds the database, which is why the timeout is generous.
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(4);

    private DistributedApplication _app = default!;
    private IPlaywright _playwright = default!;

    private IBrowser _browser = default!;

    /// <summary>One context for the run, so the WebAssembly runtime cached by one test is there for the next.</summary>
    public IBrowserContext Context { get; private set; } = default!;

    public HttpClient Web { get; private set; } = default!;

    public string WebUrl { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OutreachStudio_AppHost>();
        _app = await builder.BuildAsync().WaitAsync(StartupTimeout);
        await _app.StartAsync().WaitAsync(StartupTimeout);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("web").WaitAsync(StartupTimeout);
        await _app.ResourceNotifications.WaitForResourceAsync("providers", "Running").WaitAsync(StartupTimeout);

        Web = _app.CreateHttpClient("web");
        WebUrl = _app.GetEndpoint("web", "http").ToString().TrimEnd('/');

        // Idempotent, and the only way to get the browser without PowerShell on the build machine.
        Microsoft.Playwright.Program.Main(["install", "chromium"]);
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
        Context = await _browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1440, Height = 900 } });
    }

    public async ValueTask DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
        await _app.DisposeAsync();
    }

    public async Task<IPage> NewPageAsync()
    {
        var page = await Context.NewPageAsync();
        page.SetDefaultTimeout(30_000);
        return page;
    }
}

[CollectionDefinition("stack")]
public sealed class StackCollection : ICollectionFixture<StackFixture>;
