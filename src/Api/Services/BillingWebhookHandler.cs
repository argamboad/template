using Microsoft.EntityFrameworkCore;
using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Core.Repositories;

namespace Template.Api.Services;

public enum WebhookResult { Applied, Duplicate, Ignored, InvalidSignature }

/// <summary>
/// Applies inbound billing webhooks to the tenant's <see cref="Subscription"/> projection (BILLING-3,
/// ADR-006). The flow is: <b>verify signature</b> → <b>dedup via the inbox</b> (ADR-007) →
/// <b>EnterTenant</b> (ADR-003) → <b>upsert</b> through the normal tenant-scoped path. The inbox claim
/// and the projection write commit in one transaction, so a redelivery re-processes only if the apply
/// failed. Completing checkout grants nothing — THIS is what flips entitlements.
/// </summary>
public sealed class BillingWebhookHandler(
    IBillingProvider provider,
    IInbox inbox,
    IRepository<Subscription> subscriptions,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<WebhookResult> HandleAsync(string payload, string? signature, CancellationToken cancellationToken = default)
    {
        BillingWebhookEvent? evt;
        try
        {
            evt = provider.ParseWebhookEvent(payload, signature);
        }
        catch (BillingWebhookSignatureException)
        {
            return WebhookResult.InvalidSignature; // not authentic — reject, apply nothing
        }

        if (evt is null)
            return WebhookResult.Ignored; // authentic but irrelevant — acknowledge

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // At-least-once + out-of-order delivery: claim the event id so a redelivery is a no-op.
        if (!await inbox.TryClaimAsync("stripe", evt.EventId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return WebhookResult.Duplicate;
        }

        // No JWT here — enter the (signature-authenticated) tenant so the write is stamped + scoped
        // by the normal interceptor/filter, not the cross-tenant escape hatch (ADR-003).
        using (tenantContext.EnterTenant(evt.TenantId))
        {
            await UpsertSubscriptionAsync(evt, cancellationToken);
            await transaction.CommitAsync(cancellationToken); // claim + projection commit atomically
        }

        return WebhookResult.Applied;
    }

    private async Task UpsertSubscriptionAsync(BillingWebhookEvent evt, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var subscription = await subscriptions.Query().FirstOrDefaultAsync(cancellationToken); // entered-tenant scoped

        if (subscription is null)
        {
            await subscriptions.AddAsync(new Subscription
            {
                PlanKey = evt.PlanKey,
                Status = evt.Status,
                StripeCustomerId = evt.StripeCustomerId,
                StripeSubscriptionId = evt.StripeSubscriptionId,
                CurrentPeriodEnd = evt.CurrentPeriodEnd,
                CreatedAt = now,
                UpdatedAt = now,
            }, cancellationToken); // TenantId stamped to the entered tenant by the interceptor
        }
        else
        {
            subscription.PlanKey = evt.PlanKey;
            subscription.Status = evt.Status;
            subscription.StripeCustomerId = evt.StripeCustomerId;
            subscription.StripeSubscriptionId = evt.StripeSubscriptionId;
            subscription.CurrentPeriodEnd = evt.CurrentPeriodEnd;
            subscription.UpdatedAt = now;
            subscriptions.Update(subscription);
        }

        await subscriptions.SaveChangesAsync(cancellationToken);
    }
}
