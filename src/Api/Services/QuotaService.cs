using Microsoft.EntityFrameworkCore;
using Template.Core.Abstractions;
using Template.Core.Billing;
using Template.Core.Entities;
using Template.Core.Repositories;

namespace Template.Api.Services;

/// <summary>
/// Resolves and enforces plan quotas for the current tenant (BILLING-5). The plan is resolved from the
/// tenant's <see cref="Subscription"/> exactly like <see cref="EntitlementService"/> (fail-closed to Free);
/// seat usage counts members + pending invites; metered usage is a per-month counter. Limits live in the
/// <see cref="PlanCatalog"/> — a missing limit means unlimited, so this is inert until a plan sets one.
/// </summary>
public sealed class QuotaService(
    IRepository<Subscription> subscriptions,
    ITenantRepository tenants,
    ITenantInvitationRepository invitations,
    IRepository<UsageCounter> usage,
    ICurrentTenant currentTenant,
    TimeProvider clock) : IQuotaService
{
    public async Task<SeatUsage> GetSeatUsageAsync(CancellationToken cancellationToken = default)
    {
        var plan = await ResolvePlanAsync(cancellationToken);
        var tenantId = currentTenant.TenantId ?? Guid.Empty;

        // Seats = current members + still-pending invites (a pending invite is a reserved seat, so N
        // invites can't over-provision past the cap).
        var members = (await tenants.GetMembersAsync(tenantId, cancellationToken)).Count;
        var pending = (await invitations.GetPendingForTenantAsync(tenantId, cancellationToken)).Count;
        return new SeatUsage(members + pending, plan.SeatLimit);
    }

    public async Task<bool> CanAddSeatsAsync(int count = 1, CancellationToken cancellationToken = default)
        => (await GetSeatUsageAsync(cancellationToken)).CanAdd(count);

    public async Task<bool> TryConsumeAsync(string usageKey, int amount = 1, CancellationToken cancellationToken = default)
    {
        var plan = await ResolvePlanAsync(cancellationToken);
        if (plan.UsageLimit(usageKey) is not { } limit)
            return true; // unlimited for this key — allow without tracking

        var now = clock.GetUtcNow();
        var period = now.ToString("yyyy-MM");
        var counter = await usage.Query()
            .FirstOrDefaultAsync(c => c.Key == usageKey && c.Period == period, cancellationToken);

        var current = counter?.Count ?? 0;
        if (current + amount > limit)
            return false; // would exceed — deny, do not increment

        if (counter is null)
            await usage.AddAsync(new UsageCounter { Key = usageKey, Period = period, Count = amount, UpdatedAt = now }, cancellationToken);
        else
        {
            counter.Count += amount;
            counter.UpdatedAt = now;
            usage.Update(counter);
        }

        await usage.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<Plan> ResolvePlanAsync(CancellationToken cancellationToken)
    {
        var subscription = await subscriptions.Query().FirstOrDefaultAsync(cancellationToken);
        return PlanCatalog.Get(EntitlementService.ResolvePlanKey(subscription, clock.GetUtcNow()));
    }
}
