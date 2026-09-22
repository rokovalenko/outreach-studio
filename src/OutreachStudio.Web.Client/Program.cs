using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using OutreachStudio.Web.Client.Api;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();

// One client for the whole app, pointed at the host that served the page.
builder.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// The audience is downloaded once and kept, the editor api is a thin call per action.
builder.Services.AddSingleton<IAudienceSource, HttpAudienceSource>();
builder.Services.AddScoped<ICampaignEditorApi, HttpCampaignEditorApi>();

await builder.Build().RunAsync();
