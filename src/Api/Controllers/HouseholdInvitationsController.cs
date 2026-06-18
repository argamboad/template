using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Template.Api.Models;
using Template.Api.Services;
using Template.Core.Entities;
using Template.Core.Repositories;

namespace Template.Api.Controllers;

/// <summary>
/// Tenant ("household") invitations. Owners invite by email, list, regenerate and
/// revoke pending invites; any authenticated user accepts with a token to join.
/// The raw token is surfaced once on create/regenerate (also emailed to the
/// invitee); the list never returns tokens.
/// </summary>
[ApiController]
[Route("api/household/invitations")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class HouseholdInvitationsController(
    ITenantInvitationService service,
    ITenantRepository tenants,
    IErrorResponseFactory errorFactory) : ControllerBase
{
    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out userId);

    private async Task<TenantMembership?> GetMembershipAsync() =>
        TryGetUserId(out var uid) ? await tenants.GetMembershipAsync(uid) : null;

    /// <summary>Creates an invitation (owner only). Returns the raw token once and emails it.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvitationRequest request)
    {
        var membership = await GetMembershipAsync();
        if (membership == null)
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));
        if (!IsOwner(membership))
            return Forbid403("Only the household owner can invite members");

        try
        {
            var (invitation, rawToken) = await service.CreateAsync(membership.TenantId, membership.UserId, request.Email ?? "");
            return Created($"/api/household/invitations/{invitation.Id}", CreateInvitationResponse.From(invitation, rawToken));
        }
        catch (InvitationException ex)
        {
            return MapError(ex);
        }
    }

    /// <summary>Lists the tenant's pending invitations (owner only). Token is not returned.</summary>
    [HttpGet]
    public async Task<IActionResult> GetPending()
    {
        var membership = await GetMembershipAsync();
        if (membership == null)
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));
        if (!IsOwner(membership))
            return Forbid403("Only the household owner can view invitations");

        var pending = await service.GetPendingAsync(membership.TenantId);
        return Ok((IReadOnlyList<InvitationResponse>)pending.Select(InvitationResponse.From).ToList());
    }

    /// <summary>Regenerates the token for a pending invitation (owner only). Issues a new raw token once.</summary>
    [HttpPost("{id:guid}/regenerate")]
    public async Task<IActionResult> Regenerate(Guid id)
    {
        var membership = await GetMembershipAsync();
        if (membership == null)
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));
        if (!IsOwner(membership))
            return Forbid403("Only the household owner can regenerate invitation tokens");

        try
        {
            var result = await service.RegenerateAsync(membership.TenantId, id, membership.UserId);
            if (result is null)
                return NotFound(errorFactory.CreateError("invitation_not_found", "Invitation not found"));
            var (invitation, rawToken) = result.Value;
            return Ok(CreateInvitationResponse.From(invitation, rawToken));
        }
        catch (InvitationException ex)
        {
            return MapError(ex);
        }
    }

    /// <summary>Revokes a pending invitation (owner only).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var membership = await GetMembershipAsync();
        if (membership == null)
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));
        if (!IsOwner(membership))
            return Forbid403("Only the household owner can revoke invitations");

        try
        {
            var found = await service.RevokeAsync(membership.TenantId, id);
            return found
                ? NoContent()
                : NotFound(errorFactory.CreateError("invitation_not_found", "Invitation not found"));
        }
        catch (InvitationException ex)
        {
            return MapError(ex);
        }
    }

    /// <summary>Accepts an invitation by token, joining the tenant (any member).</summary>
    [HttpPost("accept")]
    public async Task<IActionResult> Accept([FromBody] AcceptInvitationRequest request)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));

        try
        {
            await service.AcceptAsync(userId, request.Token ?? "");
            return NoContent();
        }
        catch (InvitationException ex)
        {
            return MapError(ex);
        }
    }

    private static bool IsOwner(TenantMembership m) =>
        string.Equals(m.Role, TenantRoles.Owner, StringComparison.OrdinalIgnoreCase);

    private IActionResult Forbid403(string message) =>
        StatusCode(StatusCodes.Status403Forbidden, errorFactory.CreateError("forbidden", message));

    private IActionResult MapError(InvitationException ex) =>
        ex.Error == "forbidden"
            ? Forbid403(ex.Message)
            : ex.Conflict
                ? Conflict(errorFactory.CreateError(ex.Error, ex.Message))
                : BadRequest(errorFactory.CreateError(ex.Error, ex.Message));
}
