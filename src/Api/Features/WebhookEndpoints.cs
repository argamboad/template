using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Template.Api.Configuration;
using Template.Api.Services;
using Template.Core.Authorization;
using Template.Core.Entities;
using Template.Infrastructure.Webhooks;

namespace Template.Api.Features;

/// <summary>
/// Outbound webhook management (HOOKS, ADR-016): owner-only routes to register/list/remove subscriptions
/// and fire a **test** delivery. Mapped only when HOOKS is enabled (off ⇒ the routes 404). Real events are
/// emitted by features calling <see cref="IWebhookPublisher.PublishAsync"/> (async via the outbox); the
/// test route delivers synchronously so the owner sees the endpoint's response immediately.
/// </summary>
public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookManagement(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/webhooks")
            .RequireAuthorization(AuthPolicies.TenantApi)
            .RequirePermission(Permission.ManageWebhooks) // owner-only (RBAC, ADR-009)
            .WithTags("Webhooks");

        group.MapGet("/", async (IWebhookSubscriptionService svc, CancellationToken ct) =>
            Results.Ok((await svc.ListAsync(ct)).Select(WebhookResponse.From).ToList()));

        group.MapPost("/", async (CreateWebhookRequest request, IWebhookSubscriptionService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = CurrentUserId(http);
            if (userId is null)
                return Results.Unauthorized();

            var created = await svc.CreateAsync(userId.Value, request.Url ?? "", request.EventTypes, ct);
            return created is null
                ? Results.BadRequest(new { error = "invalid_request", message = "A valid https URL and at least one known event type are required." })
                : Results.Created($"/api/webhooks/{created.Subscription.Id}", WebhookResponse.FromCreated(created)); // secret shown once
        });

        group.MapDelete("/{id:guid}", async (Guid id, IWebhookSubscriptionService svc, CancellationToken ct) =>
            await svc.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // Synchronous "send test" (like Stripe's) — POSTs a signed ping and returns the endpoint's status.
        group.MapPost("/{id:guid}/test", async (
            Guid id, IWebhookSubscriptionService svc, IWebhookSender sender, IWebhookSecretProtector protector,
            TimeProvider clock, CancellationToken ct) =>
        {
            var subscription = await svc.GetAsync(id, ct);
            if (subscription is null)
                return Results.NotFound();

            var secret = protector.Unprotect(subscription.EncryptedSecret);
            var eventId = Guid.CreateVersion7().ToString();
            var body = JsonSerializer.Serialize(new
            {
                id = eventId,
                type = WebhookEvents.Ping,
                created_at = clock.GetUtcNow(),
                data = new { message = "This is a test event from your app." },
            });

            try
            {
                var status = await sender.SendAsync(subscription.Url, secret, WebhookEvents.Ping, eventId, body, ct);
                return Results.Ok(new { delivered = status is >= 200 and < 300, status_code = status });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { delivered = false, error = ex.Message });
            }
        });

        // Delivery log (HOOKS-2): recent attempts for a subscription — the tenant's debug trail.
        group.MapGet("/{id:guid}/deliveries", async (Guid id, IWebhookSubscriptionService svc, CancellationToken ct) =>
            Results.Ok((await svc.ListDeliveriesAsync(id, ct)).Select(WebhookDeliveryResponse.From).ToList()));

        // Replay (HOOKS-2): re-enqueue a past delivery's exact payload (async via the outbox).
        group.MapPost("/deliveries/{deliveryId:guid}/replay", async (Guid deliveryId, IWebhookSubscriptionService svc, CancellationToken ct) =>
            await svc.ReplayAsync(deliveryId, ct) ? Results.Accepted() : Results.NotFound());

        return app;
    }

    private static Guid? CurrentUserId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

public sealed record CreateWebhookRequest
{
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("event_types")] public string[]? EventTypes { get; init; }
}

public sealed record WebhookResponse
{
    [JsonPropertyName("id")] public required Guid Id { get; init; }
    [JsonPropertyName("url")] public required string Url { get; init; }
    [JsonPropertyName("event_types")] public required IReadOnlyList<string> EventTypes { get; init; }
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("disabled_at")] public DateTimeOffset? DisabledAt { get; init; }

    /// <summary>The signing secret — present ONLY in the create response, never on list.</summary>
    [JsonPropertyName("secret")] public string? Secret { get; init; }

    public static WebhookResponse From(WebhookSubscription s) => new()
    {
        Id = s.Id,
        Url = s.Url,
        EventTypes = s.EventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        CreatedAt = s.CreatedAt,
        DisabledAt = s.DisabledAt,
    };

    public static WebhookResponse FromCreated(WebhookCreated created) => From(created.Subscription) with { Secret = created.Secret };
}

public sealed record WebhookDeliveryResponse
{
    [JsonPropertyName("id")] public required Guid Id { get; init; }
    [JsonPropertyName("event_type")] public required string EventType { get; init; }
    [JsonPropertyName("event_id")] public required string EventId { get; init; }
    [JsonPropertyName("success")] public required bool Success { get; init; }
    [JsonPropertyName("status_code")] public int? StatusCode { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }

    public static WebhookDeliveryResponse From(WebhookDelivery d) => new()
    {
        Id = d.Id,
        EventType = d.EventType,
        EventId = d.EventId,
        Success = d.Success,
        StatusCode = d.StatusCode,
        Error = d.Error,
        CreatedAt = d.CreatedAt,
    };
}
