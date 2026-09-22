using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OutreachStudio.Engine.Audience;
using OutreachStudio.Data;
using OutreachStudio.Web.Client.Api;
using OutreachStudio.Web.Services;

namespace OutreachStudio.Web.Api;

/// <summary>A receipt the providers service posts back some seconds after a send.</summary>
public sealed record ReceiptPost(string MessageId, string Type, DateTimeOffset At);

/// <summary>
/// The JSON the browser half of the editor and the providers service talk to. Enums go over the
/// wire as names so the audience payload reads the same in the browser as it does in psql, and so
/// the dictionary keyed by event type keeps its keys.
/// </summary>
public static class Endpoints
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static WebApplication MapApi(this WebApplication app)
    {
        app.MapGet("/api/campaigns/{id:guid}", async (Guid id, CampaignService campaigns, CancellationToken ct) =>
            Results.Json(await campaigns.GetEditorModelAsync(id, ct), Json));

        app.MapPut("/api/campaigns/{id:guid}", async (Guid id, HttpRequest request, CampaignService campaigns, CancellationToken ct) =>
        {
            var edit = await request.ReadFromJsonAsync<CampaignEdit>(Json, ct);
            if (edit is null)
            {
                return Results.BadRequest();
            }
            return Results.Json(await campaigns.SaveAsync(id, edit, ct), Json);
        });

        app.MapPost("/api/campaigns/{id:guid}/submit", async (Guid id, CampaignService campaigns, CancellationToken ct) =>
        {
            await campaigns.SubmitAsync(id, ct);
            return Results.NoContent();
        });

        // The whole user base in the engine's binary format (see AudienceSnapshotFormat), compressed
        // on the way out. The browser keeps it for the session so the live count never calls back.
        app.MapGet("/api/audience", async (AudienceCache audience, CancellationToken ct) =>
        {
            var users = await audience.LoadAsync(ct);
            var stream = new MemoryStream();
            AudienceSnapshotFormat.Write(stream, users);
            stream.Position = 0;
            return Results.Stream(stream, "application/octet-stream");
        });

        app.MapPost("/api/receipts", async (ReceiptPost receipt, OutreachDbContext db, CancellationToken ct) =>
        {
            var delivery = await db.Deliveries.FirstOrDefaultAsync(d => d.ProviderMessageId == receipt.MessageId, ct);
            if (delivery is null)
            {
                return Results.NotFound();
            }
            // Receipts arrive late, out of order and sometimes twice, so a state only moves forward.
            switch (receipt.Type)
            {
                case "delivered" when delivery.Status is DeliveryStatus.Sent:
                    delivery.Status = DeliveryStatus.Delivered;
                    delivery.DeliveredAt = receipt.At;
                    break;
                case "opened" when delivery.Status is DeliveryStatus.Sent or DeliveryStatus.Delivered:
                    delivery.Status = DeliveryStatus.Opened;
                    delivery.OpenedAt = receipt.At;
                    delivery.DeliveredAt ??= receipt.At;
                    break;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        app.MapGet("/api/chaos", async (ChaosClient chaos, CancellationToken ct) =>
            Results.Json(await chaos.GetAsync(ct), Json));

        app.MapPut("/api/chaos/{provider}", async (string provider, HttpRequest request, ChaosClient chaos, CancellationToken ct) =>
        {
            var settings = await request.ReadFromJsonAsync<ProviderChaos>(Json, ct);
            if (settings is null)
            {
                return Results.BadRequest();
            }
            var updated = await chaos.SetAsync(settings with { Provider = provider }, ct);
            return updated is null ? Results.NotFound() : Results.Json(updated, Json);
        });

        return app;
    }
}

/// <summary>
/// Turns the state machine's refusals into 409 and an unknown id into 404, so a client of the JSON
/// endpoints reads a status code instead of a stack trace.
/// </summary>
public sealed class InvalidStateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException
                                   && context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = ex is KeyNotFoundException
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
    }
}
