using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Perezosoft.Api.Models;
using Perezosoft.Api.Services;
using Perezosoft.Core.Abstractions;
using Perezosoft.Core.Entities;
using Perezosoft.Core.Repositories;

namespace Perezosoft.Api.Controllers;

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
    ITenantRepository tenants,
    ITenantContext tenantContext,
    IAuditLog audit,
    IRepository<Subscription> subscriptions,
    IRepository<AuditEvent> auditEvents,
    IUserRepository users,
    IJwtTokenService jwt,
    INotificationService notifications,
    IOutbox outbox,
    IUnitOfWork unitOfWork) : AdminApiControllerBase(staff)
{
    // Impersonation tokens are deliberately short-lived and non-refreshable (ADR-014).
    private static readonly TimeSpan ImpersonationLifetime = TimeSpan.FromMinutes(15);

    private const int AnnounceTitleMaxLength = 200;
    private const int AnnounceBodyMaxLength = 2000;

    /// <summary>
    /// Whether the authenticated caller is platform staff. Unlike the other actions this never 403s —
    /// any signed-in user gets <c>{ is_staff }</c> so the client can decide whether to show the admin UI.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (CurrentUserId is null)
            return Unauthorized(new ErrorResponse("invalid_token", "Invalid user identity"));

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
                return NotFound(new ErrorResponse("tenant_not_found", "Tenant not found"));

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
    /// Sends an announcement to a tenant's members (staff only; ADMIN-3). With no <c>user_ids</c> it
    /// reaches every member; with a non-empty list it targets just those members (ids that aren't
    /// members of this tenant are ignored). Delivery goes through the normal notification fan-out —
    /// in-app row and/or outbox email per each user's preferences — and the send is loudly audited in
    /// the target tenant. Everything commits atomically.
    /// </summary>
    [HttpPost("tenants/{id:guid}/announce")]
    public async Task<IActionResult> Announce(Guid id, [FromBody] AdminAnnounceRequest request, CancellationToken cancellationToken)
    {
        var (staffUserId, denied) = await RequireStaffAsync(cancellationToken);
        if (denied is not null)
            return denied;

        var title = request.Title?.Trim();
        var body = request.Body?.Trim();
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(body)
            || title.Length > AnnounceTitleMaxLength || body.Length > AnnounceBodyMaxLength)
            return BadRequest(new ErrorResponse("invalid_request",
                $"Title (max {AnnounceTitleMaxLength} chars) and body (max {AnnounceBodyMaxLength} chars) are required."));

        // Notifications, outbox emails, and the in-tenant audit all commit together.
        await using var scope = await unitOfWork.BeginTransactionAsync(cancellationToken);
        using (tenantContext.EnterTenant(id))
        {
            var tenant = await tenants.GetByIdAsync(id, cancellationToken);
            if (tenant is null)
                return NotFound(new ErrorResponse("tenant_not_found", "Tenant not found"));

            // Optional subset: notify only the requested members (intersect with actual membership so a
            // stray/non-member id can't notify someone outside this tenant). No list ⇒ every member.
            var members = await tenants.GetMembersAsync(id, cancellationToken);
            var recipients = (request.UserIds is { Count: > 0 } wanted
                ? members.Where(m => wanted.Contains(m.UserId))
                : members).ToList();

            foreach (var member in recipients)
                await notifications.NotifyAsync(member.UserId, "announcement", title, body,
                    metadata: null, cancellationToken);

            await audit.RecordAsync("admin.announcement.sent", staffUserId, nameof(Tenant), id.ToString(),
                new { member_count = recipients.Count, targeted = request.UserIds is { Count: > 0 } }, cancellationToken);
            await auditEvents.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);

            return Ok(new AdminAnnounceResponse { NotifiedCount = recipients.Count });
        }
    }

    /// <summary>
    /// Broadcasts an announcement to EVERY user across all tenants (staff only; ADMIN-3). The fan-out
    /// can be large, so it is enqueued on the outbox and delivered out-of-band — this returns 202
    /// immediately. Not tenant-audited: the action spans all tenants and the audit trail is per-tenant,
    /// so the durable outbox message is the record of the broadcast.
    /// </summary>
    [HttpPost("announce-all")]
    public async Task<IActionResult> AnnounceAll([FromBody] AdminAnnounceRequest request, CancellationToken cancellationToken)
    {
        var (_, denied) = await RequireStaffAsync(cancellationToken);
        if (denied is not null)
            return denied;

        var title = request.Title?.Trim();
        var body = request.Body?.Trim();
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(body)
            || title.Length > AnnounceTitleMaxLength || body.Length > AnnounceBodyMaxLength)
            return BadRequest(new ErrorResponse("invalid_request",
                $"Title (max {AnnounceTitleMaxLength} chars) and body (max {AnnounceBodyMaxLength} chars) are required."));

        // Stage the fan-out message and commit it. The handler (AdminBroadcastOutboxHandler) does the
        // per-user delivery out-of-band. SaveChangesAsync flushes the staged message before commit
        // (CommitAsync itself does not save).
        await using var scope = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await outbox.EnqueueAsync(AdminBroadcastOutboxHandler.MessageType,
            JsonSerializer.Serialize(new AdminBroadcastPayload(title, body)), tenantId: null, cancellationToken);
        await auditEvents.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Accepted(new AdminBroadcastResponse { Status = "queued" });
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
            return NotFound(new ErrorResponse("user_not_found", "User not found"));

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
