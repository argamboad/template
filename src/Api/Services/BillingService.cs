using Template.Api.Configuration;
using Template.Core.Abstractions;
using Template.Core.Billing;

namespace Template.Api.Services;

public enum CheckoutOutcome { Created, InvalidPlan }

public sealed record CheckoutResult(CheckoutOutcome Outcome, string? Url);

/// <summary>
/// Billing orchestration behind the platform <c>BillingController</c> (ADR-006): validates the
/// requested plan and creates a hosted checkout session via the configured <see cref="IBillingProvider"/>.
/// Owner-only access is enforced by the controller (<c>TenantApiControllerBase</c>), so this takes the
/// tenant id directly. It writes no <c>Subscription</c> — access is granted only by the BILLING-3
/// webhook, never by completing checkout.
/// </summary>
public interface IBillingService
{
    Task<CheckoutResult> CreateCheckoutAsync(Guid tenantId, string? planKey, CancellationToken cancellationToken = default);
}

public sealed class BillingService(IBillingProvider billing, IApplicationSettings app) : IBillingService
{
    public async Task<CheckoutResult> CreateCheckoutAsync(Guid tenantId, string? planKey, CancellationToken cancellationToken = default)
    {
        if (!IsPurchasablePlan(planKey))
            return new(CheckoutOutcome.InvalidPlan, null);

        var baseUrl = app.ClientUrl.TrimEnd('/');
        var session = await billing.CreateCheckoutSessionAsync(
            new BillingCheckoutRequest(tenantId, planKey!, $"{baseUrl}/billing/success", $"{baseUrl}/billing/cancel"),
            cancellationToken);

        return new(CheckoutOutcome.Created, session.Url);
    }

    /// <summary>A non-Free plan that actually exists in the catalog (unknown keys resolve to Free).</summary>
    private static bool IsPurchasablePlan(string? planKey) =>
        !string.IsNullOrWhiteSpace(planKey) && planKey != PlanKeys.Free && PlanCatalog.Get(planKey).Key == planKey;
}
