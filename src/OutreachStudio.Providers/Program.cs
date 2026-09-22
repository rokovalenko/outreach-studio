using Microsoft.AspNetCore.Mvc;
using OutreachStudio.Providers;
using OutreachStudio.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddSingleton<ChaosState>();
builder.Services.AddSingleton<ReceiptSimulator>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReceiptSimulator>());
builder.Services.AddHttpClient("web", http => http.BaseAddress = new Uri("http://web"));

var app = builder.Build();
app.MapDefaultEndpoints();

// Four mock providers behind one endpoint shape. push-a and email-a are the primaries, the b's are
// the failover targets. Nothing leaves this process except the simulated receipts.
app.MapPost("/{provider}/send", async (string provider, SendRequest request, ChaosState chaos, ReceiptSimulator receipts, CancellationToken ct) =>
{
    var settings = chaos.Get(provider);
    if (settings is null)
    {
        return Results.NotFound(new { error = $"No provider '{provider}'" });
    }
    if (settings.LatencyMs > 0)
    {
        await Task.Delay(settings.LatencyMs, ct);
    }
    if (settings.Down)
    {
        return Results.Json(new { error = $"{provider} is down for maintenance" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    if (settings.ErrorRate > 0 && Random.Shared.NextDouble() < settings.ErrorRate)
    {
        return Results.Json(new { error = $"{provider} rejected the message (injected error)" }, statusCode: StatusCodes.Status502BadGateway);
    }
    await Task.Delay(Random.Shared.Next(20, 120), ct);
    var messageId = $"{provider}-{Guid.NewGuid():N}";
    receipts.Accepted(provider, messageId);
    return Results.Ok(new SendResponse(messageId));
});

app.MapGet("/chaos", (ChaosState chaos) => chaos.All);

app.MapPut("/chaos/{provider}", (string provider, [FromBody] ProviderChaos settings, ChaosState chaos) =>
    chaos.Set(settings with { Provider = provider }) ? Results.Ok(chaos.Get(provider)) : Results.NotFound());

app.Run();
