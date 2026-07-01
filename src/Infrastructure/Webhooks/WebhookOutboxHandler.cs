using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Webhooks;

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
    IWebhookSecretProtector protector) : IOutboxHandler
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
        var status = await sender.SendAsync(subscription.Url, secret, payload.EventType, payload.EventId, payload.Body, cancellationToken);
        if (status is < 200 or >= 300)
            throw new InvalidOperationException($"Webhook delivery to {subscription.Url} returned HTTP {status}.");
    }
}
