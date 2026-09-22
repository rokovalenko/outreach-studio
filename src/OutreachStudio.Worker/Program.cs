using OutreachStudio.Data;
using OutreachStudio.ServiceDefaults;
using OutreachStudio.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<OutreachDbContext>("outreach");
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));

// The send policy owns the retries and records every call, so the shared resilience handler would
// retry behind its back and hide provider failures. Removing it is still an experimental API.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<IProviderClient, HttpProviderClient>(http => http.BaseAddress = new Uri("http://providers"))
    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<DeliveryPipeline>();
builder.Services.AddHostedService<OutboxWorker>();

var host = builder.Build();
host.Run();
