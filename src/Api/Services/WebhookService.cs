using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Core.Repositories;
using Template.Infrastructure.Webhooks;

namespace Template.Api.Services;

/// <summary>A freshly created subscription plus its signing secret (shown once, never stored plaintext).</summary>
public sealed record WebhookCreated(WebhookSubscription Subscription, string Secret);

/// <summary>
/// Manages a tenant's outbound webhook subscriptions (HOOKS, ADR-016). Runs in the current tenant's scope
/// (owner-gated at the endpoint). Generates a signing secret at creation (returned once; stored encrypted),
/// validates the target URL and event types.
/// </summary>
public interface IWebhookSubscriptionService
{
    Task<WebhookCreated?> CreateAsync(Guid createdByUserId, string url, IEnumerable<string>? eventTypes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookSubscription>> ListAsync(CancellationToken cancellationToken = default);
    Task<WebhookSubscription?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class WebhookSubscriptionService(
    IRepository<WebhookSubscription> subscriptions,
    ITokenGenerator tokenGenerator,
    IWebhookSecretProtector protector,
    TimeProvider clock) : IWebhookSubscriptionService
{
    public async Task<WebhookCreated?> CreateAsync(Guid createdByUserId, string url, IEnumerable<string>? eventTypes, CancellationToken cancellationToken = default)
    {
        if (!IsValidUrl(url))
            return null;

        var types = NormalizeEventTypes(eventTypes);
        if (types.Count == 0)
            return null;

        var secret = "whsec_" + tokenGenerator.GenerateToken();
        var subscription = new WebhookSubscription
        {
            Url = url.Trim(),
            EventTypes = string.Join(',', types),
            EncryptedSecret = protector.Protect(secret),
            CreatedByUserId = createdByUserId,
            CreatedAt = clock.GetUtcNow(),
        };
        await subscriptions.AddAsync(subscription, cancellationToken); // TenantId stamped by the interceptor
        await subscriptions.SaveChangesAsync(cancellationToken);
        return new WebhookCreated(subscription, secret);
    }

    public async Task<IReadOnlyList<WebhookSubscription>> ListAsync(CancellationToken cancellationToken = default) =>
        await subscriptions.Query().OrderByDescending(s => s.CreatedAt).ToListAsync(cancellationToken);

    public Task<WebhookSubscription?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        subscriptions.Query().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var subscription = await subscriptions.Query().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (subscription is null)
            return false;

        subscriptions.Remove(subscription);
        await subscriptions.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool IsValidUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    // Keep only known event types; empty request defaults to all known events.
    private static IReadOnlyList<string> NormalizeEventTypes(IEnumerable<string>? eventTypes)
    {
        var requested = (eventTypes ?? WebhookEvents.Known)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(WebhookEvents.Known.Contains)
            .Distinct()
            .ToList();
        return requested.Count == 0 ? WebhookEvents.Known.ToList() : requested;
    }
}

/// <summary>
/// The seam a feature calls to emit an outbound event (HOOKS, ADR-016): fans the event out to every active
/// subscription in the current tenant that wants it, enqueuing one durable <c>"webhook"</c> outbox message
/// per subscription (staged on the caller's unit of work, so it commits with the triggering change). The
/// outbox dispatcher then signs + POSTs each, with retry/backoff for free (ADR-007).
/// </summary>
public interface IWebhookPublisher
{
    Task PublishAsync(string eventType, object data, CancellationToken cancellationToken = default);
}

public sealed class WebhookPublisher(
    IRepository<WebhookSubscription> subscriptions,
    IOutbox outbox,
    ICurrentTenant currentTenant,
    TimeProvider clock) : IWebhookPublisher
{
    public async Task PublishAsync(string eventType, object data, CancellationToken cancellationToken = default)
    {
        // Active subscriptions (tenant-scoped); the event-type match is in-memory (stored comma-separated).
        var active = (await subscriptions.Query().Where(s => s.DisabledAt == null).ToListAsync(cancellationToken))
            .Where(s => s.Subscribes(eventType))
            .ToList();
        if (active.Count == 0)
            return;

        var tenantId = currentTenant.TenantId;
        foreach (var subscription in active)
        {
            var eventId = Guid.CreateVersion7().ToString();
            var body = JsonSerializer.Serialize(new
            {
                id = eventId,
                type = eventType,
                created_at = clock.GetUtcNow(),
                data,
            });
            var payload = new WebhookOutboxPayload(subscription.Id, eventType, eventId, body);
            await outbox.EnqueueAsync(WebhookOutboxHandler.MessageType, JsonSerializer.Serialize(payload), tenantId, cancellationToken);
        }
    }
}
