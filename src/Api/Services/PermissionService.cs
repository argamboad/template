using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Template.Core.Abstractions;
using Template.Core.Authorization;
using Template.Core.Repositories;

namespace Template.Api.Services;

/// <summary>
/// Resolves the authenticated caller's tenant membership (by the <see cref="ClaimTypes.NameIdentifier"/>
/// claim) and answers permission checks via the <see cref="RolePermissions"/> matrix (ADR-009).
/// <b>Fails closed</b>: no HTTP context, no authenticated user, or no membership all yield
/// <c>false</c>. Registered request-scoped (mirrors <see cref="EntitlementService"/>).
/// </summary>
public sealed class PermissionService(IHttpContextAccessor accessor, ITenantRepository tenants) : IPermissionService
{
    public async Task<bool> HasAsync(Permission permission, CancellationToken cancellationToken = default)
    {
        var userIdValue = accessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
            return false;

        var membership = await tenants.GetMembershipAsync(userId, cancellationToken);
        return membership is not null && RolePermissions.Grants(membership.Role, permission);
    }
}
