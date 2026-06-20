using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Core.Repositories;

namespace Template.Api.Services;

public enum RemoveMemberResult { Removed, NotAMember }
public enum TransferResult { Transferred, TargetNotMember, ConcurrentModification }

/// <summary>Outcome of a leave attempt.</summary>
public enum LeaveOutcome
{
    /// <summary>A non-owner member left; old tenant + data persist.</summary>
    Left,
    /// <summary>Owner tried to leave while members remain.</summary>
    MustTransferFirst,
    /// <summary>Sole owner leaving needs an explicit confirm to wipe.</summary>
    ConfirmationRequired,
    /// <summary>Sole owner left; tenant + data wiped.</summary>
    Dissolved
}

/// <summary>
/// Tenant management writes: rename, remove-member, ownership-transfer, and leave.
/// Role enforcement lives at the controller (the role matrix). Re-home semantics
/// keep every user in exactly one tenant.
/// </summary>
public interface ITenantService
{
    /// <summary>Renames the tenant. False when it doesn't exist.</summary>
    Task<bool> RenameAsync(Guid tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a member from the tenant and lands them in a fresh tenant-of-one
    /// (owner) so they are never tenant-less. Their contributed data stays with
    /// the tenant.
    /// </summary>
    Task<RemoveMemberResult> RemoveMemberAsync(Guid tenantId, Guid targetUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transfers ownership to an existing member — the target becomes owner, the
    /// caller becomes member, in one transaction (single-owner invariant).
    /// </summary>
    Task<TransferResult> TransferOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid targetUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Leaves the caller's tenant. A non-owner member leaves (data stays); an owner
    /// with members remaining is refused (<see cref="LeaveOutcome.MustTransferFirst"/>);
    /// the sole member (owner) dissolves the tenant + wipes its data, but only with
    /// <paramref name="confirmDissolve"/> (else <see cref="LeaveOutcome.ConfirmationRequired"/>).
    /// In every non-refused case the user lands in a fresh empty tenant-of-one.
    /// </summary>
    Task<LeaveOutcome> LeaveAsync(Guid userId, bool confirmDissolve, CancellationToken cancellationToken = default);
}

public class TenantService(
    ITenantRepository tenants,
    IUnitOfWork unitOfWork,
    IEnumerable<ITenantDataContributor> dataContributors,
    TimeProvider clock,
    ILogger<TenantService> logger) : ITenantService
{
    // The tenant a re-homed user lands in (UI label is "Household").
    private const string ReHomeTenantName = "My Household";

    public async Task<bool> RenameAsync(Guid tenantId, string name, CancellationToken cancellationToken = default)
    {
        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null) return false;
        tenant.Name = name.Trim();
        await tenants.UpdateTenantAsync(tenant, cancellationToken);
        logger.LogInformation("Tenant {TenantId} renamed", tenantId);
        return true;
    }

    public async Task<RemoveMemberResult> RemoveMemberAsync(Guid tenantId, Guid targetUserId, CancellationToken cancellationToken = default)
    {
        var membership = await tenants.GetMembershipAsync(targetUserId, cancellationToken);
        if (membership == null || membership.TenantId != tenantId)
            return RemoveMemberResult.NotAMember;

        // Drop the membership and re-home the user atomically.
        await using var scope = await unitOfWork.BeginTransactionAsync(cancellationToken);

        await tenants.RemoveMemberAsync(membership, cancellationToken);
        await ReHomeAsync(targetUserId, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        logger.LogInformation("Member {UserId} removed from tenant {TenantId}; re-homed",
            targetUserId, tenantId);
        return RemoveMemberResult.Removed;
    }

    public async Task<TransferResult> TransferOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid targetUserId, CancellationToken cancellationToken = default)
    {
        var members = await tenants.GetMembersAsync(tenantId, cancellationToken);
        if (members.All(m => m.UserId != targetUserId)) return TransferResult.TargetNotMember;

        // Conditional update guards the single-owner invariant under concurrency.
        await using var scope = await unitOfWork.BeginTransactionAsync(cancellationToken);

        if (!await tenants.TryTransferOwnershipAsync(tenantId, currentOwnerUserId, targetUserId, cancellationToken))
            return TransferResult.ConcurrentModification;

        await scope.CommitAsync(cancellationToken);

        logger.LogInformation("Tenant {TenantId} ownership transferred {From} -> {To}",
            tenantId, currentOwnerUserId, targetUserId);
        return TransferResult.Transferred;
    }

    public async Task<LeaveOutcome> LeaveAsync(Guid userId, bool confirmDissolve, CancellationToken cancellationToken = default)
    {
        var membership = await tenants.GetMembershipAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("User has no tenant");
        var tenantId = membership.TenantId;
        var members = await tenants.GetMembersAsync(tenantId, cancellationToken);

        // Sole member (necessarily the owner) → dissolution.
        if (members.Count == 1)
        {
            if (!confirmDissolve) return LeaveOutcome.ConfirmationRequired;

            // Wipe + re-home atomically — a partial wipe is impossible. Each feature's
            // domain data goes first (its contributor), then the platform's core teardown.
            await using var scope = await unitOfWork.BeginTransactionAsync(cancellationToken);
            foreach (var contributor in dataContributors)
                await contributor.WipeAsync(tenantId, cancellationToken);
            await tenants.WipeDataAsync(tenantId, cancellationToken);
            await ReHomeAsync(userId, cancellationToken);
            await scope.CommitAsync(cancellationToken);

            logger.LogInformation("Tenant {TenantId} dissolved by sole owner {UserId}", tenantId, userId);
            return LeaveOutcome.Dissolved;
        }

        // Owner can't strand a multi-member tenant — transfer first.
        if (string.Equals(membership.Role, TenantRoles.Owner, StringComparison.OrdinalIgnoreCase))
            return LeaveOutcome.MustTransferFirst;

        // Non-owner member leaves; the tenant + its data stay.
        await using (var scope = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            await tenants.RemoveMemberAsync(membership, cancellationToken);
            await ReHomeAsync(userId, cancellationToken);
            await scope.CommitAsync(cancellationToken);
        }

        logger.LogInformation("Member {UserId} left tenant {TenantId}", userId, tenantId);
        return LeaveOutcome.Left;
    }

    // Lands a departing user in a fresh empty tenant-of-one (owner) so they are
    // never tenant-less.
    private async Task ReHomeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var newTenantId = Guid.CreateVersion7();
        await tenants.CreateAsync(new Tenant
        {
            Id = newTenantId,
            Name = ReHomeTenantName,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);
        await tenants.AddMemberAsync(new TenantMembership
        {
            Id = Guid.CreateVersion7(),
            TenantId = newTenantId,
            UserId = userId,
            Role = TenantRoles.Owner,
            JoinedAt = now
        }, cancellationToken);
    }
}
