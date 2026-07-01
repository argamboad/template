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
    IRepository<AuditEvent> auditEvents,
    IUserRepository users,
    IJwtTokenService jwt) : AdminApiControllerBase(staff, errorFactory)
{
    // Impersonation tokens are deliberately short-lived and non-refreshable (ADR-014).
    private static readonly TimeSpan ImpersonationLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Whether the authenticated caller is platform staff. Unlike the other actions this never 403s —
    /// any signed-in user gets <c>{ is_staff }</c> so the client can decide whether to show the admin UI.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (CurrentUserId is null)
            return Unauthorized(ErrorFactory.CreateError("invalid_token", "Invalid user identity"));

        return Ok(new AdminStatusResponse { IsStaff = await IsCurrentUserStaffAsync(cancellationToken) });
    }

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

    /// <summary>
    /// "Sign in as" a user (staff only). Returns a <b>short-lived, non-refreshable</b> access token
    /// carrying the target's identity + an <c>impersonated_by</c> claim; loudly audited in the target's
    /// tenant. No refresh token is issued, so it expires on its own.
    /// </summary>
    [HttpPost("impersonate/{userId:guid}")]
    public async Task<IActionResult> Impersonate(Guid userId, CancellationToken cancellationToken)
    {
        var (staffUserId, denied) = await RequireStaffAsync(cancellationToken);
        if (denied is not null)
            return denied;

        var target = await users.GetByIdAsync(userId, cancellationToken);
        if (target is null)
            return NotFound(ErrorFactory.CreateError("user_not_found", "User not found"));

        var membership = await tenants.GetMembershipAsync(userId, cancellationToken);
        var tenantId = membership?.TenantId;
        var tenantName = tenantId is { } tid ? (await tenants.GetByIdAsync(tid, cancellationToken))?.Name : null;

        var token = jwt.IssueImpersonationToken(
            target.Id, target.Email, staffUserId, ImpersonationLifetime, target.DisplayName, tenantName, tenantId);

        // Loud, in-tenant audit so the target tenant can see a platform admin signed in as this user.
        if (tenantId is { } t)
            using (tenantContext.EnterTenant(t))
            {
                await audit.RecordAsync("admin.impersonation.started", staffUserId, nameof(User), userId.ToString(), null, cancellationToken);
                await auditEvents.SaveChangesAsync(cancellationToken);
            }

        return Ok(new ImpersonationResponse
        {
            AccessToken = token,
            ExpiresIn = (int)ImpersonationLifetime.TotalSeconds,
        });
    }
}
