using Template.Api.Configuration;
using Template.Core.Abstractions;
using Template.Core.Billing;
using Template.Core.Entities;
using Template.Core.Repositories;

namespace Template.Api.Features.Billing;

public enum CheckoutStatus { Created, Unauthenticated, Forbidden, InvalidPlan }

public sealed record CheckoutResult(CheckoutStatus Status, string? Url);

/// <summary>
/// Starts a hosted checkout to subscribe the caller's tenant to a paid plan (BILLING-2, ADR-006).
/// <b>Owner-only</b> — reuses the membership/role gate the household controllers use. Returns a
/// provider checkout URL; it does NOT grant access — only the resulting webhook (BILLING-3) flips the
/// tenant's <see cref="Subscription"/> to active.
/// </summary>
public sealed class BillingHandler(ITenantRepository tenants, IBillingProvider billing, IApplicationSettings app)
{
    public async Task<CheckoutResult> CreateCheckoutAsync(Guid? currentUserId, string? planKey, CancellationToken cancellationToken)
    {
        if (currentUserId is not { } userId)
            return new(CheckoutStatus.Unauthenticated, null);

        var membership = await tenants.GetMembershipAsync(userId, cancellationToken);
        if (membership is null)
            return new(CheckoutStatus.Unauthenticated, null);

        if (!string.Equals(membership.Role, TenantRoles.Owner, StringComparison.OrdinalIgnoreCase))
            return new(CheckoutStatus.Forbidden, null);

        if (!IsPurchasablePlan(planKey))
            return new(CheckoutStatus.InvalidPlan, null);

        var baseUrl = app.ClientUrl.TrimEnd('/');
        var session = await billing.CreateCheckoutSessionAsync(
            new BillingCheckoutRequest(membership.TenantId, planKey!, $"{baseUrl}/billing/success", $"{baseUrl}/billing/cancel"),
            cancellationToken);

        return new(CheckoutStatus.Created, session.Url);
    }

    /// <summary>A non-Free plan that actually exists in the catalog (unknown keys resolve to Free).</summary>
    private static bool IsPurchasablePlan(string? planKey) =>
        !string.IsNullOrWhiteSpace(planKey) && planKey != PlanKeys.Free && PlanCatalog.Get(planKey).Key == planKey;
}
