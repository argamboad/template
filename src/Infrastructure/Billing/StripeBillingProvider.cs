using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using Template.Core.Abstractions;

namespace Template.Infrastructure.Billing;

/// <summary>
/// Stripe reference implementation of <see cref="IBillingProvider"/> (ADR-006). Creates a hosted
/// Checkout session in subscription mode for the plan's configured price, tagging it with the tenant
/// id (<c>ClientReferenceId</c> + metadata) so the webhook (BILLING-3) reconciles it. No card data
/// touches our system — the money mutation happens on Stripe.
/// </summary>
public sealed class StripeBillingProvider(IOptions<StripeSettings> options) : IBillingProvider
{
    private readonly StripeSettings _settings = options.Value;

    public async Task<BillingCheckoutSession> CreateCheckoutSessionAsync(BillingCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        if (!_settings.Prices.TryGetValue(request.PlanKey, out var priceId) || string.IsNullOrWhiteSpace(priceId))
            throw new InvalidOperationException($"No Stripe price configured for plan '{request.PlanKey}' (Billing:Stripe:Prices).");

        // apiBase is null in prod (real Stripe) and set to stripe-mock in tests.
        var client = new StripeClient(_settings.SecretKey, apiBase: _settings.ApiBase);
        var sessions = new SessionService(client);

        var session = await sessions.CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            ClientReferenceId = request.TenantId.ToString(),
            Metadata = new Dictionary<string, string> { ["tenant_id"] = request.TenantId.ToString() },
        }, cancellationToken: cancellationToken);

        return new BillingCheckoutSession(session.Url);
    }
}
