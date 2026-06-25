namespace Template.Core.Abstractions;

/// <summary>
/// Abstraction over the payment provider (ADR-006), mirroring <c>IEmailSender</c>'s Core-abstraction
/// shape so Core stays provider-agnostic and tests run offline. The Stripe reference implementation
/// lives in Infrastructure; <c>FakeBillingProvider</c> is the in-memory test/dev fallback. Money
/// mutations happen on the provider's hosted Checkout — we never build card forms or store card data.
/// </summary>
public interface IBillingProvider
{
    /// <summary>
    /// Creates a hosted checkout session to subscribe the tenant to a plan and returns its URL. The
    /// session carries the tenant id so the resulting webhook (BILLING-3) can reconcile it back to the
    /// tenant. Access is NOT granted by completing checkout — only the webhook flips the subscription.
    /// </summary>
    Task<BillingCheckoutSession> CreateCheckoutSessionAsync(BillingCheckoutRequest request, CancellationToken cancellationToken = default);
}

/// <param name="TenantId">The tenant being subscribed — carried into the session for webhook reconciliation.</param>
/// <param name="PlanKey">Plan to subscribe to (a <c>PlanKeys</c> value); the provider maps it to a price.</param>
/// <param name="SuccessUrl">Where the provider returns the user after a completed checkout.</param>
/// <param name="CancelUrl">Where the provider returns the user if they cancel.</param>
public sealed record BillingCheckoutRequest(Guid TenantId, string PlanKey, string SuccessUrl, string CancelUrl);

/// <summary>The hosted checkout URL the client redirects to.</summary>
public sealed record BillingCheckoutSession(string Url);
