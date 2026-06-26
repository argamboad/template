using Microsoft.EntityFrameworkCore;
using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Core.Repositories;

namespace Template.Infrastructure.Audit;

/// <summary>
/// Tenant-data hook so the audit trail participates in tenant dissolve (OBS-4, ADR-008). Uses the
/// audited cross-tenant escape hatch (the dissolve runs for a tenant other than the current one) and a
/// set-based delete — which bypasses the append-only interceptor, so dissolve can purge a tenant's
/// events even though application code can't. (If a regulatory legal-hold is needed, export before wipe —
/// see the GDPR backlog item.)
/// </summary>
public sealed class AuditDataContributor(IRepository<AuditEvent> events) : ITenantDataContributor
{
    public Task<bool> HasDataAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        events.QueryAllTenants().AnyAsync(e => e.TenantId == tenantId, cancellationToken);

    public async Task WipeAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await events.QueryAllTenants()
            .Where(e => e.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);
}
