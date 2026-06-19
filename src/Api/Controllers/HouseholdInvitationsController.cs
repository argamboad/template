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

    private async Task<TenantMembership?> GetMembershipAsync(CancellationToken cancellationToken = default) =>
        TryGetUserId(out var uid) ? await tenants.GetMembershipAsync(uid, cancellationToken) : null;

    /// <summary>Creates an invitation (owner only). Returns the raw token once and emails it.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvitationRequest request, CancellationToken cancellationToken)
    {
        var membership = await GetMembershipAsync(cancellationToken);
        if (membership == null)
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));
        if (!IsOwner(membership))
            return Forbid403("Only the household owner can invite members");

        var result = await service.CreateAsync(membership.TenantId, membership.UserId, request.Email ?? "", cancellationToken);
        return result.Status switch
        {
            InviteCreateStatus.Created => Created(
                $"/api/household/invitations/{result.Invitation!.Id}",
                CreateInvitationResponse.From(result.Invitation, result.RawToken!)),
            InviteCreateStatus.AlreadyMember => Conflict(
                errorFactory.CreateError("already_member", "That email is already a member of this household")),
            _ => BadRequest(errorFactory.CreateError("invalid_request", "A valid email is required")),
        };
    }

    /// <summary>Lists the tenant's pending invitations (owner only). Token is not returned.</summary>
    [HttpGet]
    public async Task<IActionResult> GetPending(CancellationToken cancellationToken)
    {
        var membership = await GetMembershipAsync(cancellationToken);
        if (membership == null)
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));
        if (!IsOwner(membership))
            return Forbid403("Only the household owner can view invitations");

        var pending = await service.GetPendingAsync(membership.TenantId, cancellationToken);
        return Ok((IReadOnlyList<InvitationResponse>)pending.Select(InvitationResponse.From).ToList());
    }

    /// <summary>Regenerates the token for a pending invitation (owner only). Issues a new raw token once.</summary>
    [HttpPost("{id:guid}/regenerate")]
    public async Task<IActionResult> Regenerate(Guid id, CancellationToken cancellationToken)
    {
        var membership = await GetMembershipAsync(cancellationToken);
        if (membership == null)
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));
        if (!IsOwner(membership))
            return Forbid403("Only the household owner can regenerate invitation tokens");

        var result = await service.RegenerateAsync(membership.TenantId, id, membership.UserId, cancellationToken);
        return result.Status switch
        {
            InviteRegenerateStatus.Regenerated => Ok(
                CreateInvitationResponse.From(result.Invitation!, result.RawToken!)),
            InviteRegenerateStatus.NotFound => NotFound(
                errorFactory.CreateError("invitation_not_found", "Invitation not found")),
            _ => BadRequest(
                errorFactory.CreateError("invitation_not_pending", "Only pending invitations can be regenerated")),
        };
    }

    /// <summary>Revokes a pending invitation (owner only).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        var membership = await GetMembershipAsync(cancellationToken);
        if (membership == null)
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));
        if (!IsOwner(membership))
            return Forbid403("Only the household owner can revoke invitations");

        var found = await service.RevokeAsync(membership.TenantId, id, cancellationToken);
        return found
            ? NoContent()
            : NotFound(errorFactory.CreateError("invitation_not_found", "Invitation not found"));
    }

    /// <summary>Accepts an invitation by token, joining the tenant (any member).</summary>
    [HttpPost("accept")]
    public async Task<IActionResult> Accept([FromBody] AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));

        var status = await service.AcceptAsync(userId, request.Token ?? "", cancellationToken);
        return status switch
        {
            AcceptStatus.Joined => NoContent(),
            AcceptStatus.AlreadyMember => Conflict(
                errorFactory.CreateError("already_member", "You are already a member of this household")),
            AcceptStatus.MustTransferFirst => BadRequest(
                errorFactory.CreateError("must_transfer_first", "Transfer ownership before joining another household")),
            AcceptStatus.WouldAbandonData => BadRequest(
                errorFactory.CreateError("would_abandon_data", "Your household has data — transfer or remove it before joining another")),
            AcceptStatus.NoHousehold => BadRequest(
                errorFactory.CreateError("invalid_request", "Caller has no household")),
            _ => BadRequest(
                errorFactory.CreateError("invitation_invalid", "Invitation is invalid or has expired")),
        };
    }

    private static bool IsOwner(TenantMembership m) =>
        string.Equals(m.Role, TenantRoles.Owner, StringComparison.OrdinalIgnoreCase);

    private IActionResult Forbid403(string message) =>
        StatusCode(StatusCodes.Status403Forbidden, errorFactory.CreateError("forbidden", message));
}
