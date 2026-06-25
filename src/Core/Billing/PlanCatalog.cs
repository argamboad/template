namespace Template.Core.Billing;

/// <summary>
/// A plan tier and the set of entitlement keys it grants. Plans are **code/config, not tenant data**
/// (ADR-006).
/// </summary>
public sealed record Plan(string Key, IReadOnlySet<string> Entitlements)
{
    public bool Includes(string entitlementKey) => Entitlements.Contains(entitlementKey);
}

/// <summary>Plan-tier keys. <c>Free</c> is the fail-closed default for any tenant without an active paid plan.</summary>
public static class PlanKeys
{
    public const string Free = "free";
    public const string Pro = "pro";
}

/// <summary>
/// Entitlement (feature-gate) keys. These are **example** keys for the template — replace them with the
/// app's real feature gates, then gate endpoints with <c>.RequireEntitlement(Entitlements.Xxx)</c>.
/// </summary>
public static class Entitlements
{
    public const string ProFeature = "pro-feature";
}

/// <summary>
/// The plan catalog: plan key → <see cref="Plan"/>. Edit/extend this (or move it to configuration) to
/// define the app's tiers. <see cref="Get"/> fails closed — an unknown plan key resolves to Free.
/// </summary>
public static class PlanCatalog
{
    private static readonly IReadOnlyDictionary<string, Plan> Plans = new Dictionary<string, Plan>
    {
        [PlanKeys.Free] = new(PlanKeys.Free, new HashSet<string>()),
        [PlanKeys.Pro] = new(PlanKeys.Pro, new HashSet<string> { Entitlements.ProFeature }),
    };

    /// <summary>Resolves a plan by key; unknown keys fall back to <see cref="PlanKeys.Free"/> (fail-closed).</summary>
    public static Plan Get(string planKey) => Plans.TryGetValue(planKey, out var plan) ? plan : Plans[PlanKeys.Free];
}
