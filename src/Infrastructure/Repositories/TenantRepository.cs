using Microsoft.EntityFrameworkCore;
using Template.Core.Entities;
using Template.Core.Repositories;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="ITenantRepository"/>.</summary>
public class TenantRepository(AppDbContext db) : ITenantRepository
{
    public async Task<Guid?> GetTenantIdForUserAsync(Guid userId)
    {
        var member = await db.TenantMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId);
        return member?.TenantId;
    }

    public async Task<Tenant> CreateAsync(Tenant tenant)
    {
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    public async Task<TenantMembership> AddMemberAsync(TenantMembership member)
    {
        db.TenantMemberships.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    public async Task<Tenant?> GetByIdAsync(Guid tenantId) =>
        await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);

    public async Task<TenantMembership?> GetMembershipAsync(Guid userId) =>
        await db.TenantMemberships.FirstOrDefaultAsync(m => m.UserId == userId);

    public async Task<List<TenantMembership>> GetMembersAsync(Guid tenantId) =>
        await db.TenantMemberships.Where(m => m.TenantId == tenantId).ToListAsync();

    public async Task<bool> IsEmailMemberAsync(Guid tenantId, string email)
    {
        // Emails are stored normalized (ToLowerInvariant on write); normalize the input the
        // same way in C# and compare to the column directly — no per-row SQL ToLower() (which
        // is culture-dependent and defeats the index).
        var normalized = email.ToLowerInvariant();
        return await (from m in db.TenantMemberships
                      join u in db.Users on m.UserId equals u.Id
                      where m.TenantId == tenantId && u.Email == normalized
                      select m.Id).AnyAsync();
    }

    public async Task UpdateMemberAsync(TenantMembership member)
    {
        db.TenantMemberships.Update(member);
        await db.SaveChangesAsync();
    }

    public async Task<bool> TryTransferOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid targetUserId)
    {
        // EF InMemory doesn't support ExecuteUpdateAsync; fall back to tracked entity
        // updates. Unit tests are single-threaded so there is no concurrency risk there.
        if (!db.Database.IsRelational())
        {
            var currentOwner = await db.TenantMemberships.FirstOrDefaultAsync(
                m => m.TenantId == tenantId && m.UserId == currentOwnerUserId && m.Role == TenantRoles.Owner);
            if (currentOwner is null) return false;
            var target = await db.TenantMemberships.FirstOrDefaultAsync(
                m => m.TenantId == tenantId && m.UserId == targetUserId);
            if (target is null) return false;
            currentOwner.Role = TenantRoles.Member;
            target.Role = TenantRoles.Owner;
            await db.SaveChangesAsync();
            return true;
        }

        // Relational path: guard on the current owner's role first.
        // If 0 rows are affected, someone else already changed the owner (race lost).
        var affected = await db.TenantMemberships
            .Where(m => m.TenantId == tenantId && m.UserId == currentOwnerUserId && m.Role == TenantRoles.Owner)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, TenantRoles.Member));
        if (affected == 0) return false;

        // Unconditional flip on the target — pre-validated by the service layer.
        await db.TenantMemberships
            .Where(m => m.TenantId == tenantId && m.UserId == targetUserId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, TenantRoles.Owner));
        return true;
    }

    public async Task DeleteTenantAsync(Guid tenantId)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null) return;
        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync();
    }

    // TODO: the template has no domain tables yet. When real tenant-scoped tables
    // exist, return true here if any of them hold data for the tenant.
    public Task<bool> HasDataAsync(Guid tenantId) => Task.FromResult(false);

    public async Task<List<TenantMemberDetail>> GetMemberDetailsAsync(Guid tenantId) =>
        await (from m in db.TenantMemberships
               join u in db.Users on m.UserId equals u.Id
               where m.TenantId == tenantId
               orderby u.Email
               select new TenantMemberDetail(m.UserId, u.DisplayName, u.Email, m.Role, m.JoinedAt))
            .ToListAsync();

    public async Task UpdateTenantAsync(Tenant tenant)
    {
        db.Tenants.Update(tenant);
        await db.SaveChangesAsync();
    }

    public async Task RemoveMemberAsync(TenantMembership member)
    {
        db.TenantMemberships.Remove(member);
        await db.SaveChangesAsync();
    }

    public async Task WipeDataAsync(Guid tenantId)
    {
        // TODO: the template has no domain tables yet. When real tenant-scoped tables
        // exist, RemoveRange them here (dependents first) so the wipe is exhaustive.
        db.TenantInvitations.RemoveRange(db.TenantInvitations.Where(i => i.TenantId == tenantId));
        db.TenantMemberships.RemoveRange(db.TenantMemberships.Where(m => m.TenantId == tenantId));

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant != null) db.Tenants.Remove(tenant);

        await db.SaveChangesAsync(); // one transaction → all-or-nothing
    }
}
