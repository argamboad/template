using System.Collections.Concurrent;
using Template.Core.Abstractions;

namespace Template.Infrastructure.Billing;

/// <summary>
/// In-memory <see cref="IBillingProvider"/> for tests and for dev when no Stripe key is configured
/// (ADR-006): returns a deterministic fake checkout URL and records each request, so behaviour can be
/// asserted offline with zero real charges. Keeps the app bootable with no Stripe setup.
/// </summary>
public sealed class FakeBillingProvider : IBillingProvider
{
    /// <summary>Every checkout request received, for assertions.</summary>
    public ConcurrentQueue<BillingCheckoutRequest> Requests { get; } = new();

    public Task<BillingCheckoutSession> CreateCheckoutSessionAsync(BillingCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Enqueue(request);
        return Task.FromResult(new BillingCheckoutSession($"https://billing.test/checkout/{request.TenantId}/{request.PlanKey}"));
    }
}
