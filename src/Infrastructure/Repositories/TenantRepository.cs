using Microsoft.EntityFrameworkCore;
using Template.Core.Entities;
using Template.Core.Repositories;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="ITenantRepository"/>.</summary>
public class TenantRepository(AppDbContext db) : ITenantRepository
{
    public async Task<Guid?> GetTenantIdForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await db.TenantMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        return member?.TenantId;
    }

    public async Task<Tenant> CreateAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
        return tenant;
    }

    public async Task<TenantMembership> AddMemberAsync(TenantMembership member, CancellationToken cancellationToken = default)
    {
        db.TenantMemberships.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        return member;
    }

    public async Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

    public async Task<TenantMembership?> GetMembershipAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.TenantMemberships.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);

    public async Task<List<TenantMembership>> GetMembersAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await db.TenantMemberships.Where(m => m.TenantId == tenantId).ToListAsync(cancellationToken);

    public async Task<bool> IsEmailMemberAsync(Guid tenantId, string email, CancellationToken cancellationToken = default)
    {
        // Emails are stored normalized (ToLowerInvariant on write); normalize the input the
        // same way in C# and compare to the column directly — no per-row SQL ToLower() (which
        // is culture-dependent and defeats the index).
        var normalized = email.ToLowerInvariant();
        return await (from m in db.TenantMemberships
                      join u in db.Users on m.UserId equals u.Id
                      where m.TenantId == tenantId && u.Email == normalized
                      select m.Id).AnyAsync(cancellationToken);
    }

    public async Task UpdateMemberAsync(TenantMembership member, CancellationToken cancellationToken = default)
    {
        db.TenantMemberships.Update(member);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryTransferOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid targetUserId, CancellationToken cancellationToken = default)
    {
        // Guard on the current owner's role first. If 0 rows are affected, someone else
        // already changed the owner (race lost).
        var affected = await db.TenantMemberships
            .Where(m => m.TenantId == tenantId && m.UserId == currentOwnerUserId && m.Role == TenantRoles.Owner)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, TenantRoles.Member), cancellationToken);
        if (affected == 0) return false;

        // Unconditional flip on the target — pre-validated by the service layer.
        await db.TenantMemberships
            .Where(m => m.TenantId == tenantId && m.UserId == targetUserId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, TenantRoles.Owner), cancellationToken);
        return true;
    }

    public async Task DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant == null) return;
        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync(cancellationToken);
    }

    // TODO: the template has no domain tables yet. When real tenant-scoped tables
    // exist, return true here if any of them hold data for the tenant.
    public Task<bool> HasDataAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public async Task<List<TenantMemberDetail>> GetMemberDetailsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await (from m in db.TenantMemberships
               join u in db.Users on m.UserId equals u.Id
               where m.TenantId == tenantId
               orderby u.Email
               select new TenantMemberDetail(m.UserId, u.DisplayName, u.Email, m.Role, m.JoinedAt))
            .ToListAsync(cancellationToken);

    public async Task UpdateTenantAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        db.Tenants.Update(tenant);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveMemberAsync(TenantMembership member, CancellationToken cancellationToken = default)
    {
        db.TenantMemberships.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task WipeDataAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // TODO: the template has no domain tables yet. When real tenant-scoped tables
        // exist, RemoveRange them here (dependents first) so the wipe is exhaustive.
        db.TenantInvitations.RemoveRange(db.TenantInvitations.Where(i => i.TenantId == tenantId));
        db.TenantMemberships.RemoveRange(db.TenantMemberships.Where(m => m.TenantId == tenantId));

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant != null) db.Tenants.Remove(tenant);

        await db.SaveChangesAsync(cancellationToken); // one transaction → all-or-nothing
    }
}
