using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Services;

namespace Template.Api.Controllers;

/// <summary>
/// Base for the platform-staff admin surface (ADR-014). Authenticated as a normal user (the app JWT),
/// then gated per-request by the staff allowlist — there is no separate admin login. Derived controllers
/// call <see cref="RequireStaffAsync"/> first; a non-staff caller gets <b>403</b>. Staff membership is
/// out-of-band config, never a tenant role.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public abstract class AdminApiControllerBase(IPlatformStaffService staff, IErrorResponseFactory errorFactory) : ControllerBase
{
    protected IErrorResponseFactory ErrorFactory { get; } = errorFactory;

    protected Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>
    /// Non-gating staff check: true when the current caller is platform staff. Unlike
    /// <see cref="RequireStaffAsync"/> this never produces a 403 — for the status probe the client uses to
    /// decide whether to show the admin UI.
    /// </summary>
    protected async Task<bool> IsCurrentUserStaffAsync(CancellationToken cancellationToken)
        => CurrentUserId is { } userId && await staff.IsStaffAsync(userId, cancellationToken);

    /// <summary>
    /// Gate for admin actions: returns the staff user id when the caller is platform staff, otherwise a
    /// ready-to-return 401 (no identity) / 403 (not staff).
    /// </summary>
    protected async Task<(Guid StaffUserId, IActionResult? Denied)> RequireStaffAsync(CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } userId)
            return (default, Unauthorized(ErrorFactory.CreateError("invalid_token", "Invalid user identity")));

        if (!await staff.IsStaffAsync(userId, cancellationToken))
            return (default, StatusCode(StatusCodes.Status403Forbidden,
                ErrorFactory.CreateError("forbidden", "Platform-staff only")));

        return (userId, null);
    }
}
