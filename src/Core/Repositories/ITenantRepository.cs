using Template.Core.Entities;

namespace Template.Core.Repositories;

/// <summary>
/// Tenants and their membership. Identity (logins, inboxes) stays user-scoped;
/// this repository owns the user→tenant resolution that app-data scoping depends
/// on.
/// </summary>
public interface ITenantRepository
{
    /// <summary>
    /// Resolves the caller's tenant id via membership; null when the user has no
    /// membership (callers fail closed — never an unscoped read).
    /// </summary>
    Task<Guid?> GetTenantIdForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Tenant> CreateAsync(Tenant tenant, CancellationToken cancellationToken = default);
    Task<TenantMembership> AddMemberAsync(TenantMembership member, CancellationToken cancellationToken = default);

    Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>The user's single membership (tenant + role), or null.</summary>
    Task<TenantMembership?> GetMembershipAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<List<TenantMembership>> GetMembersAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>True if any member of the tenant has the given (normalized) email.</summary>
    Task<bool> IsEmailMemberAsync(Guid tenantId, string email, CancellationToken cancellationToken = default);

    Task UpdateMemberAsync(TenantMembership member, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically flips <paramref name="currentOwnerUserId"/> from 'owner' to 'member'
    /// and <paramref name="targetUserId"/> to 'owner', guarded by a conditional update
    /// on the current owner's role. Returns false if the current owner no longer holds
    /// the 'owner' role (race lost — caller must abort its transaction scope).
    /// Must be called inside an active <see cref="IUnitOfWork"/> transaction scope.
    /// </summary>
    Task<bool> TryTransferOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid targetUserId, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes a tenant (cascades its data) — used when a solo
    /// tenant-of-one is dissolved as its owner joins another.</summary>
    Task DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>The tenant's members joined to their user display info.</summary>
    Task<List<TenantMemberDetail>> GetMemberDetailsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task UpdateTenantAsync(Tenant tenant, CancellationToken cancellationToken = default);

    Task RemoveMemberAsync(TenantMembership member, CancellationToken cancellationToken = default);

    /// <summary>
    /// Core tenant teardown in one transaction: removes invitations, memberships, and the
    /// tenant row. Feature/domain data is wiped separately by each
    /// <see cref="Template.Core.Abstractions.ITenantDataContributor"/>; user-scoped data
    /// (logins, tokens) is untouched.
    /// </summary>
    Task WipeDataAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>A roster row: membership joined to the user's display info.</summary>
public record TenantMemberDetail(
    Guid UserId, string? DisplayName, string Email, string Role, DateTimeOffset JoinedAt);
