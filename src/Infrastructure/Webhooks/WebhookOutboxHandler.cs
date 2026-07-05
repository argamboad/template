using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Perezosoft.Core.Abstractions;
using Perezosoft.Core.Entities;
using Perezosoft.Infrastructure.Persistence;

namespace Perezosoft.Infrastructure.Webhooks;

/// <summary>The outbox payload for a single webhook delivery (one per subscription × event).</summary>
public sealed record WebhookOutboxPayload(Guid SubscriptionId, string EventType, string EventId, string Body);

/// <summary>
/// Outbox handler for <c>"webhook"</c> messages (HOOKS, ADR-016): delivers one signed POST to the target
/// subscription. A non-2xx (or transport error) throws, so the outbox retries with backoff and
/// dead-letters after the cap — no bespoke retry logic. If the subscription was removed or disabled since
/// the event was enqueued, the delivery is a no-op (treated as done, not retried). Idempotent: a duplicate
/// dispatch just re-POSTs, which the receiver dedups by the <c>X-Webhook-Id</c> header.
/// </summary>
public sealed class WebhookOutboxHandler(
    AppDbContext db,
    IWebhookSender sender,
    IWebhookSecretProtector protector,
    TimeProvider clock) : IOutboxHandler
{
    public const string MessageType = "webhook";
    public string Type => MessageType;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Deserialize<WebhookOutboxPayload>(message.Payload)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an unreadable webhook payload.");

        // The outbox is tenant-less, so bypass the tenant filter to load the target subscription by id.
        var subscription = await db.Set<WebhookSubscription>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == payload.SubscriptionId, cancellationToken);
        if (subscription is null || !subscription.IsActive)
            return; // removed/disabled since enqueue — nothing to deliver, don't retry

        var secret = protector.Unprotect(subscription.EncryptedSecret);

        int? status = null;
        string? transportError = null;
        try
        {
            status = await sender.SendAsync(subscription.Url, secret, payload.EventType, payload.EventId, payload.Body, cancellationToken);
        }
        catch (Exception ex)
        {
            transportError = ex.Message; // network/timeout — no HTTP status
        }

        var success = status is >= 200 and < 300;

        // Record the attempt (HOOKS-2). Added to the shared context; the OutboxProcessor's SaveChanges
        // commits it together with the message's sent/retry outcome (whether we return or throw below).
        db.Set<WebhookDelivery>().Add(new WebhookDelivery
        {
            TenantId = message.TenantId ?? subscription.TenantId,
            SubscriptionId = subscription.Id,
            EventType = payload.EventType,
            EventId = payload.EventId,
            Body = payload.Body,
            Success = success,
            StatusCode = status,
            Error = success ? null : transportError ?? $"HTTP {status}",
            CreatedAt = clock.GetUtcNow(),
        });

        if (!success)
            throw new InvalidOperationException(
                transportError ?? $"Webhook delivery to {subscription.Url} returned HTTP {status}."); // → outbox retry
    }
}
