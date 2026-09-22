using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using MudBlazor.Services;
using OutreachStudio.Data;
using OutreachStudio.Data.Seeding;
using OutreachStudio.ServiceDefaults;
using OutreachStudio.Web.Api;
using OutreachStudio.Web.Client.Api;
using OutreachStudio.Web.Components;
using OutreachStudio.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
// Retries are off because the schedule pass opens its own transaction and a retrying execution
// strategy refuses to run inside one.
builder.AddNpgsqlDbContext<OutreachDbContext>("outreach", settings => settings.DisableRetry = true, OutreachDbContext.Configure);
// Messages come up in the bottom corner because the page actions live in the top one.
builder.Services.AddMudServices(mud =>
{
    mud.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    mud.SnackbarConfiguration.VisibleStateDuration = 3000;
});
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// The audience download is a few megabytes of JSON. Only that content type is compressed, so the
// pre-compressed static assets keep being served as they are.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ["application/json"];
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

builder.Services.AddHttpClient("providers", http => http.BaseAddress = new Uri("http://providers"));

builder.Services.AddSingleton<AudienceCache>();
builder.Services.AddSingleton<IAudienceSource>(sp => sp.GetRequiredService<AudienceCache>());
builder.Services.AddSingleton<DeliveryFeed>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeliveryFeed>());
builder.Services.AddScoped<CampaignService>();
builder.Services.AddScoped<ICampaignEditorApi, ServerCampaignEditorApi>();
builder.Services.AddSingleton<ProviderHealth>();
builder.Services.AddScoped<SuppressionService>();
builder.Services.AddScoped<ChaosClient>();

var app = builder.Build();

// The database is created and filled here rather than in the AppHost, so a fresh clone has a
// history to look at after one dotnet run.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OutreachDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await db.Database.MigrateAsync();
    await DatabaseSeeder.SeedIfEmptyAsync(db, DateTimeOffset.UtcNow, logger, CancellationToken.None);
}

app.UseResponseCompression();
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
// A 404 from the JSON endpoints is an answer, so only the pages get the not found page rendered
// over their status code.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api"), pages =>
    pages.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseMiddleware<InvalidStateMiddleware>();
app.UseAntiforgery();

app.MapDefaultEndpoints();
app.MapStaticAssets();
app.MapApi();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(OutreachStudio.Web.Client._Imports).Assembly);

app.Run();
