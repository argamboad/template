using System.Net.Mail;
using Template.Api.Configuration;
using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Core.Repositories;
using Template.Infrastructure.Email;

namespace Template.Api.Services;

/// <summary>
/// A validation/flow failure in the invitation pipeline. <see cref="Error"/> is
/// the snake_case error code the API surfaces; <see cref="Conflict"/> picks 409
/// over the default 400.
/// </summary>
public class InvitationException(string error, string message, bool conflict = false) : Exception(message)
{
    public string Error { get; } = error;
    public bool Conflict { get; } = conflict;
}

public interface ITenantInvitationService
{
    /// <summary>Creates (or refreshes an existing pending) invitation. Caller has
    /// already been verified as the tenant's owner. Emails the invite and returns
    /// the saved invitation plus the raw token (one-time reveal — not stored).</summary>
    Task<(TenantInvitation Invitation, string RawToken)> CreateAsync(Guid tenantId, Guid inviterUserId, string email);

    Task<List<TenantInvitation>> GetPendingAsync(Guid tenantId);

    /// <summary>Revokes the old hash and issues a fresh token for an existing pending invite.
    /// Returns null if the invitation was not found for this tenant. Emails the new invite
    /// and returns the saved invitation plus the new raw token (one-time reveal).</summary>
    Task<(TenantInvitation Invitation, string RawToken)?> RegenerateAsync(Guid tenantId, Guid invitationId, Guid userId);

    /// <summary>Revokes a pending invite owned by the tenant. False = not found for
    /// this tenant (controller → 404).</summary>
    Task<bool> RevokeAsync(Guid tenantId, Guid invitationId);

    /// <summary>Redeems a token for the signed-in user, moving their membership to
    /// the inviting tenant. Throws <see cref="InvitationException"/> on any
    /// validation/flow failure.</summary>
    Task AcceptAsync(Guid userId, string token);
}

public class TenantInvitationService(
    ITenantInvitationRepository invitations,
    ITenantRepository tenants,
    ITokenGenerator tokenGenerator,
    ITokenHasher tokenHasher,
    IUnitOfWork unitOfWork,
    IEmailSender emailSender,
    IUserService userService,
    IApplicationSettings appSettings,
    IInvitationSettings invitationSettings,
    TimeProvider clock,
    ILogger<TenantInvitationService> logger) : ITenantInvitationService
{
    private TimeSpan InvitationTtl => TimeSpan.FromDays(invitationSettings.LifespanDays);

    public async Task<(TenantInvitation Invitation, string RawToken)> CreateAsync(Guid tenantId, Guid inviterUserId, string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !MailAddress.TryCreate(email.Trim(), out _))
            throw new InvitationException("invalid_request", "A valid email is required");

        var normalized = email.Trim().ToLowerInvariant();

        // Can't invite someone who's already a member of this tenant.
        if (await tenants.IsEmailMemberAsync(tenantId, normalized))
            throw new InvitationException("already_member", "That email is already a member of this household", conflict: true);

        var now = clock.GetUtcNow();
        var rawToken = tokenGenerator.GenerateToken();
        var tokenHash = tokenHasher.HashToken(rawToken);

        // A pending invite for the same (tenant, email) is refreshed, not duplicated.
        var existing = await invitations.GetPendingByEmailAsync(tenantId, normalized);
        TenantInvitation invitation;
        if (existing != null)
        {
            existing.TokenHash = tokenHash;
            existing.InvitedByUserId = inviterUserId;
            existing.CreatedAt = now;
            existing.ExpiresAt = now + InvitationTtl;
            await invitations.UpdateAsync(existing);
            logger.LogInformation("Refreshed pending invitation {Id} for tenant {TenantId}", existing.Id, tenantId);
            invitation = existing;
        }
        else
        {
            invitation = await invitations.CreateAsync(new TenantInvitation
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                InvitedEmail = normalized,
                InvitedByUserId = inviterUserId,
                Status = InvitationStatuses.Pending,
                TokenHash = tokenHash,
                CreatedAt = now,
                ExpiresAt = now + InvitationTtl
            });
            logger.LogInformation("Created invitation {Id} for tenant {TenantId}", invitation.Id, tenantId);
        }

        await SendInvitationEmailAsync(normalized, rawToken, inviterUserId);
        return (invitation, rawToken);
    }

    public async Task<(TenantInvitation Invitation, string RawToken)?> RegenerateAsync(Guid tenantId, Guid invitationId, Guid userId)
    {
        var invitation = await invitations.GetByIdUnscopedAsync(invitationId);
        // Cross-tenant treated as not-found — no existence oracle on opaque IDs.
        if (invitation == null || invitation.TenantId != tenantId) return null;
        if (invitation.Status != InvitationStatuses.Pending)
            throw new InvitationException("invitation_not_pending", "Only pending invitations can be regenerated");

        var now = clock.GetUtcNow();
        var rawToken = tokenGenerator.GenerateToken();
        invitation.TokenHash = tokenHasher.HashToken(rawToken);
        invitation.InvitedByUserId = userId;
        invitation.CreatedAt = now;
        invitation.ExpiresAt = now + InvitationTtl;
        await invitations.UpdateAsync(invitation);
        logger.LogInformation("Regenerated token for invitation {Id} (tenant {TenantId})", invitation.Id, tenantId);

        await SendInvitationEmailAsync(invitation.InvitedEmail, rawToken, userId);
        return (invitation, rawToken);
    }

    public Task<List<TenantInvitation>> GetPendingAsync(Guid tenantId) =>
        invitations.GetPendingForTenantAsync(tenantId);

    public async Task<bool> RevokeAsync(Guid tenantId, Guid invitationId)
    {
        var invitation = await invitations.GetByIdUnscopedAsync(invitationId);
        // Cross-tenant treated as not-found — no existence oracle on opaque IDs.
        if (invitation == null || invitation.TenantId != tenantId) return false;

        if (invitation.Status == InvitationStatuses.Pending)
        {
            invitation.Status = InvitationStatuses.Revoked;
            await invitations.UpdateAsync(invitation);
            logger.LogInformation("Revoked invitation {Id}", invitationId);
        }
        return true;
    }

    public async Task AcceptAsync(Guid userId, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvitationException("invitation_invalid", "Invitation is invalid");

        // Hash the presented token before lookup; never compare raw values.
        var tokenHash = tokenHasher.HashToken(token);
        var invitation = await invitations.GetByTokenHashAsync(tokenHash);
        var now = clock.GetUtcNow();

        // Unknown / revoked / accepted / past-expiry → invalid (don't leak which).
        if (invitation == null
            || invitation.Status != InvitationStatuses.Pending
            || invitation.ExpiresAt <= now)
            throw new InvitationException("invitation_invalid", "Invitation is invalid or has expired");

        var membership = await tenants.GetMembershipAsync(userId)
            ?? throw new InvitationException("invalid_request", "Caller has no household");

        if (membership.TenantId == invitation.TenantId)
            throw new InvitationException("already_member", "You are already a member of this household", conflict: true);

        var oldTenantId = membership.TenantId;
        var members = await tenants.GetMembersAsync(oldTenantId);
        var isOwner = string.Equals(membership.Role, TenantRoles.Owner, StringComparison.OrdinalIgnoreCase);
        var soloOwner = isOwner && members.Count == 1;

        // An owner of a multi-member tenant must hand off first.
        if (isOwner && members.Count > 1)
            throw new InvitationException("must_transfer_first",
                "Transfer ownership before joining another household");

        // A solo owner carrying real data can't silently abandon it.
        var dissolveOld = false;
        if (soloOwner)
        {
            if (await tenants.HasDataAsync(oldTenantId))
                throw new InvitationException("would_abandon_data",
                    "Your household has data — transfer or remove it before joining another");
            dissolveOld = true; // empty solo tenant-of-one is dissolved on join
        }

        // Move membership + consume token + dissolve old solo tenant atomically.
        await using var scope = await unitOfWork.BeginTransactionAsync();

        membership.TenantId = invitation.TenantId;
        membership.Role = TenantRoles.Member;
        membership.JoinedAt = now;
        await tenants.UpdateMemberAsync(membership);

        // Conditional flip — only one concurrent accept can update the row. If another
        // accept already won the race, the scope disposes without CommitAsync and the
        // membership move rolls back.
        if (!await invitations.TryAcceptAsync(invitation.Id))
            throw new InvitationException("invitation_invalid", "Invitation is invalid or has expired");

        if (dissolveOld)
            await tenants.DeleteTenantAsync(oldTenantId);

        await scope.CommitAsync();

        logger.LogInformation("User {UserId} accepted invitation {Id} -> tenant {TenantId}",
            userId, invitation.Id, invitation.TenantId);
    }

    private async Task SendInvitationEmailAsync(string email, string rawToken, Guid inviterUserId)
    {
        var joinUrl = $"{appSettings.ClientUrl}/join?token={Uri.EscapeDataString(rawToken)}";
        try
        {
            // Invites go out in the inviter's saved language (the recipient may have no account).
            var inviter = await userService.GetUserByIdAsync(inviterUserId);
            var emailBody = BrandedEmail.Invitation(joinUrl, rawToken, BrandedEmail.ResolveCulture(inviter?.Locale));
            await emailSender.SendAsync(email, emailBody.Subject, emailBody.Html, emailBody.InlineImages);
        }
        catch (Exception ex)
        {
            // Delivery is best-effort — the owner can still share the raw token returned
            // in the API response.
            logger.LogWarning(ex, "Failed to send invitation email to {Email}", email);
        }
    }
}
