using Microsoft.AspNetCore.Mvc;
using Template.Api.Models;
using Template.Api.Services;
using Template.Core.Authorization;
using Template.Core.Repositories;

namespace Template.Api.Controllers;

/// <summary>
/// Tenant ("household") management. Authorization goes through the permission seam (ADR-009):
/// reading needs <see cref="Permission.ViewTenant"/> (every member); rename needs
/// <see cref="Permission.RenameTenant"/> and remove-member needs <see cref="Permission.ManageMembers"/>
/// (owner/admin); transfer needs <see cref="Permission.TransferOwnership"/> (owner only). Leave is open
/// to any member. The caller's role is read from the membership, never from the request. Invitations
/// live in <see cref="HouseholdInvitationsController"/>.
/// </summary>
[ApiController]
[Route("api/household")]
public class HouseholdController(
    ITenantService service,
    ITenantRepository tenants,
    IErrorResponseFactory errorFactory) : TenantApiControllerBase(tenants, errorFactory)
{
    /// <summary>Tenant + caller role + member roster (any member).</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var membership = await GetMembershipAsync(cancellationToken);
        if (membership == null)
            return InvalidToken();

        var tenant = await Tenants.GetByIdAsync(membership.TenantId, cancellationToken);
        if (tenant == null)
            return NotFound(ErrorFactory.CreateError("household_not_found", "Household not found"));

        var members = await Tenants.GetMemberDetailsAsync(membership.TenantId, cancellationToken);
        return Ok(new TenantResponse
        {
            Id = tenant.Id,
            Name = tenant.Name,
            MyRole = membership.Role,
            Members = members.Select(TenantMemberResponse.From).ToList()
        });
    }

    /// <summary>Renames the tenant (owner only).</summary>
    [HttpPut]
    public async Task<IActionResult> Rename([FromBody] RenameTenantRequest request, CancellationToken cancellationToken)
    {
        var membership = await GetMembershipAsync(cancellationToken);
        if (membership == null)
            return InvalidToken();
        if (RequirePermission(membership, Permission.RenameTenant, "Only the household owner can rename the household") is { } forbidden)
            return forbidden;

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ErrorFactory.CreateError("invalid_request", "Name is required"));

        var renamed = await service.RenameAsync(membership.TenantId, request.Name, cancellationToken);
        if (!renamed)
            return NotFound(ErrorFactory.CreateError("household_not_found", "Household not found"));

        var tenant = await Tenants.GetByIdAsync(membership.TenantId, cancellationToken);
        var members = await Tenants.GetMemberDetailsAsync(membership.TenantId, cancellationToken);
        return Ok(new TenantResponse
        {
            Id = tenant!.Id,
            Name = tenant.Name,
            MyRole = membership.Role,
            Members = members.Select(TenantMemberResponse.From).ToList()
        });
    }

    /// <summary>Removes a member (owner only). Cannot remove self.</summary>
    [HttpDelete("members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid userId, CancellationToken cancellationToken)
    {
        var membership = await GetMembershipAsync(cancellationToken);
        if (membership == null)
            return InvalidToken();
        if (RequirePermission(membership, Permission.ManageMembers, "Only the household owner can remove members") is { } forbidden)
            return forbidden;

        // The owner leaves/transfers via the leave/transfer endpoints, not this one.
        if (userId == membership.UserId)
            return BadRequest(ErrorFactory.CreateError("invalid_request",
                "You cannot remove yourself — transfer ownership or leave instead"));

        var result = await service.RemoveMemberAsync(membership.TenantId, userId, cancellationToken);
        return result == RemoveMemberResult.Removed
            ? NoContent()
            : NotFound(ErrorFactory.CreateError("member_not_found", "That user is not a member of this household"));
    }

    /// <summary>Transfers ownership to an existing member (owner only).</summary>
    [HttpPost("transfer-ownership")]
    public async Task<IActionResult> TransferOwnership([FromBody] TransferOwnershipRequest request, CancellationToken cancellationToken)
    {
        var membership = await GetMembershipAsync(cancellationToken);
        if (membership == null)
            return InvalidToken();
        if (RequirePermission(membership, Permission.TransferOwnership, "Only the household owner can transfer ownership") is { } forbidden)
            return forbidden;

        if (request.UserId is not { } targetUserId)
            return BadRequest(ErrorFactory.CreateError("invalid_request", "user_id is required"));
        if (targetUserId == membership.UserId)
            return BadRequest(ErrorFactory.CreateError("invalid_request", "You already own this household"));

        var result = await service.TransferOwnershipAsync(membership.TenantId, membership.UserId, targetUserId, cancellationToken);
        return result switch
        {
            TransferResult.Transferred => NoContent(),
            TransferResult.TargetNotMember => NotFound(ErrorFactory.CreateError("member_not_found", "That user is not a member of this household")),
            TransferResult.ConcurrentModification => Conflict(ErrorFactory.CreateError("concurrent_modification", "Ownership was modified by a concurrent request — please retry")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, ErrorFactory.CreateError("internal_error", "Unexpected transfer result"))
        };
    }

    /// <summary>Leaves the tenant. A sole owner must confirm dissolution; an owner
    /// with members must transfer first.</summary>
    [HttpPost("leave")]
    public async Task<IActionResult> Leave([FromBody] LeaveTenantRequest? request, CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } userId)
            return InvalidToken();

        var outcome = await service.LeaveAsync(userId, request?.ConfirmDissolve ?? false, cancellationToken);
        return outcome switch
        {
            LeaveOutcome.Left or LeaveOutcome.Dissolved => NoContent(),
            LeaveOutcome.MustTransferFirst => BadRequest(ErrorFactory.CreateError(
                "must_transfer_first", "Transfer ownership before leaving — other members remain")),
            LeaveOutcome.ConfirmationRequired => Conflict(ErrorFactory.CreateError(
                "confirmation_required",
                "Leaving dissolves this household and permanently deletes its data and pending "
                + "invitations. Re-send with confirm_dissolve=true to proceed.")),
            _ => StatusCode(StatusCodes.Status500InternalServerError,
                ErrorFactory.CreateError("internal_error", "Unexpected leave outcome"))
        };
    }
}
