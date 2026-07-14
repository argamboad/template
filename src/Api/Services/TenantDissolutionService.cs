using Perezosoft.Core.Abstractions;
using Perezosoft.Core.Repositories;

namespace Perezosoft.Api.Services;

/// <summary>
/// The shared "dissolve a tenant's data" sequence (DEBT-7): fan out over every
/// <see cref="ITenantDataContributor"/> so each feature wipes its own domain data first, then run the
/// platform's core teardown (<see cref="ITenantRepository.WipeDataAsync"/>). Both the sole-owner leave
/// (<see cref="ITenantService.LeaveAsync"/>) and solo-owner account erasure
/// (<see cref="IAccountErasureService"/>) go through this so the order is identical and defined once.
/// <para>
/// This deliberately does NOT open its own transaction — callers invoke it inside their existing
/// dissolve transaction (with re-home / audit / membership steps) so the whole thing commits atomically.
/// </para>
/// </summary>
public interface ITenantDissolutionService
{
    /// <summary>
    /// Wipes the tenant's data: each contributor first, then the core teardown. Enlists in the caller's
    /// ambient transaction; it does not begin or commit one.
    /// </summary>
    Task DissolveAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class TenantDissolutionService(
    IEnumerable<ITenantDataContributor> dataContributors,
    ITenantRepository tenants) : ITenantDissolutionService
{
    public async Task DissolveAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Each feature wipes its domain data first (its contributor), then the platform's core teardown.
        foreach (var contributor in dataContributors)
            await contributor.WipeAsync(tenantId, cancellationToken);
        await tenants.WipeDataAsync(tenantId, cancellationToken);
    }
}
