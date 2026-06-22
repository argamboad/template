using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Configuration;
using Template.Api.Services;
using Template.Core.Entities;
using Template.Core.Repositories;

namespace Template.Api.Controllers;

/// <summary>
/// Base for JWT-authenticated, tenant-scoped controllers. Centralizes the
/// caller-identity and membership/role helpers every tenant feature needs, so a
/// feature slice doesn't re-copy them. The <c>[Authorize]</c> here is inherited by
/// derived controllers.
/// </summary>
[Authorize(AuthPolicies.TenantApi)]
public abstract class TenantApiControllerBase(
    ITenantRepository tenants,
    IErrorResponseFactory errorFactory) : ControllerBase
{
    /// <summary>The error-response factory, for derived controllers to build envelopes.</summary>
    protected IErrorResponseFactory ErrorFactory { get; } = errorFactory;

    /// <summary>Tenant/membership repository, for derived controllers that read tenant data.</summary>
    protected ITenantRepository Tenants { get; } = tenants;

    /// <summary>The authenticated caller's user id, or null when the token lacks/can't parse it.</summary>
    protected Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    /// <summary>The caller's single tenant membership (tenant + role), or null.</summary>
    protected Task<TenantMembership?> GetMembershipAsync(CancellationToken cancellationToken = default) =>
        CurrentUserId is { } uid
            ? Tenants.GetMembershipAsync(uid, cancellationToken)
            : Task.FromResult<TenantMembership?>(null);

    protected static bool IsOwner(TenantMembership membership) =>
        string.Equals(membership.Role, TenantRoles.Owner, StringComparison.OrdinalIgnoreCase);

    /// <summary>401 with the standard envelope — the caller's token is missing/invalid.</summary>
    protected IActionResult InvalidToken() =>
        Unauthorized(ErrorFactory.CreateError("invalid_token", "Invalid user identity"));

    /// <summary>403 with the standard envelope.</summary>
    protected IActionResult Forbid403(string message) =>
        StatusCode(StatusCodes.Status403Forbidden, ErrorFactory.CreateError("forbidden", message));
}
