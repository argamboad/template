namespace Template.Core.Entities;

/// <summary>
/// A tenant's billing subscription — a local **projection** of the payment provider's state (Stripe is
/// the source of truth for money; ADR-006). A tenant has at most one (unique on <c>TenantId</c>).
/// **Absence ⇒ Free**, and any non-active status fails closed to Free — entitlements are never granted
/// on stale/uncertain state. The Stripe ids are null until BILLING-2 attaches a paid plan.
/// </summary>
public class Subscription : ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    /// <summary>The plan this subscription grants while active — a <c>PlanKeys</c> value.</summary>
    public required string PlanKey { get; set; }

    /// <summary>One of <see cref="SubscriptionStatus"/>. Only active/trialing grant the plan.</summary>
    public required string Status { get; set; }

    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }

    /// <summary>End of the paid period; past = lapsed (fails closed to Free even if Status is active).</summary>
    public DateTimeOffset? CurrentPeriodEnd { get; set; }

    /// <summary>
    /// When the tenant was last notified that this subscription lapsed (BILLING-6). Set by the lapse
    /// sweep so it nudges once per lapse, not every run; cleared implicitly when a new period starts
    /// (the sweep re-notifies only if this predates <see cref="CurrentPeriodEnd"/>).
    /// </summary>
    public DateTimeOffset? LapseNotifiedAt { get; set; }

    /// <summary>
    /// When the provider emitted the most recently <em>applied</em> webhook event. The webhook handler
    /// applies an incoming event only if it is strictly newer than this, so a redelivered/out-of-order
    /// older event cannot clobber newer state (v2 audit LOGIC-B1). Null until the first event is applied.
    /// </summary>
    public DateTimeOffset? LastEventAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Status constants for <see cref="Subscription.Status"/> (mirrors the Stripe lifecycle).</summary>
public static class SubscriptionStatus
{
    public const string Active = "active";
    public const string Trialing = "trialing";
    public const string PastDue = "past_due";
    public const string Canceled = "canceled";
}
