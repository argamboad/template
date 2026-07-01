using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Template.Api.Models;
using Template.Api.Services;
using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Core.Repositories;

namespace Template.Api.Controllers;

/// <summary>
/// Platform-staff back-office (ADMIN-1, ADR-014): read-only cross-tenant inspection. The global tenant
/// filter is never loosened — the tenant list reads non-tenant-scoped tables directly, and the per-tenant
/// detail <b>enters the target tenant</b> (<see cref="ITenantContext.EnterTenant"/>, ADR-003) so its
/// tenant-scoped data is read through the normal filter, scoped to that tenant. Viewing a tenant is
/// <b>audited in that tenant</b>, so its owner can see a platform admin looked.
/// </summary>
[Route("api/admin")]
public class AdminController(
    IPlatformStaffService staff,
    IErrorResponseFactory errorFactory,
    ITenantRepository tenants,
    ITenantContext tenantContext,
    IAuditLog audit,
    IRepository<Subscription> subscriptions,
    IRepository<AuditEvent> auditEvents) : AdminApiControllerBase(staff, errorFactory)
{
    /// <summary>Every tenant with its member count (staff only).</summary>
    [HttpGet("tenants")]
    public async Task<IActionResult> ListTenants(CancellationToken cancellationToken)
    {
        var (_, denied) = await RequireStaffAsync(cancellationToken);
        if (denied is not null)
            return denied;

        var all = await tenants.ListAllAsync(cancellationToken);
        return Ok((IReadOnlyList<AdminTenantSummaryResponse>)all.Select(AdminTenantSummaryResponse.From).ToList());
    }

    /// <summary>One tenant's detail — members, subscription, audit volume (staff only; audited in-tenant).</summary>
    [HttpGet("tenants/{id:guid}")]
    public async Task<IActionResult> TenantDetail(Guid id, CancellationToken cancellationToken)
    {
        var (staffUserId, denied) = await RequireStaffAsync(cancellationToken);
        if (denied is not null)
            return denied;

        // Enter the target tenant so its ITenantScoped data reads through the normal filter (no bypass).
        using (tenantContext.EnterTenant(id))
        {
            var tenant = await tenants.GetByIdAsync(id, cancellationToken);
            if (tenant is null)
                return NotFound(ErrorFactory.CreateError("tenant_not_found", "Tenant not found"));

            var members = await tenants.GetMemberDetailsAsync(id, cancellationToken);
            var subscription = await subscriptions.Query().FirstOrDefaultAsync(cancellationToken);
            var auditCount = await auditEvents.Query().CountAsync(cancellationToken);

            // Loud, in-tenant audit: the tenant's owner can see a platform admin accessed the account.
            await audit.RecordAsync("admin.tenant.viewed", staffUserId, nameof(Tenant), id.ToString(), null, cancellationToken);
            await auditEvents.SaveChangesAsync(cancellationToken);

            return Ok(new AdminTenantDetailResponse
            {
                Id = tenant.Id,
                Name = tenant.Name,
                CreatedAt = tenant.CreatedAt,
                Members = members.Select(TenantMemberResponse.From).ToList(),
                SubscriptionStatus = subscription?.Status ?? "none",
                AuditEventCount = auditCount,
            });
        }
    }
}
