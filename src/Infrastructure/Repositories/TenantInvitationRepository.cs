using Microsoft.EntityFrameworkCore;
using Template.Core.Entities;
using Template.Core.Repositories;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="ITenantInvitationRepository"/>.</summary>
public class TenantInvitationRepository(AppDbContext db) : ITenantInvitationRepository
{
    public async Task<TenantInvitation> CreateAsync(TenantInvitation invitation)
    {
        db.TenantInvitations.Add(invitation);
        await db.SaveChangesAsync();
        return invitation;
    }

    // Unscoped by design: fetches across tenants so the service can treat a
    // cross-tenant id as "not found" after an explicit TenantId check. Bypasses the
    // global tenant filter.
    public async Task<TenantInvitation?> GetByIdUnscopedAsync(Guid id) =>
        await db.TenantInvitations.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id);

    public async Task<List<TenantInvitation>> GetPendingForTenantAsync(Guid tenantId) =>
        await db.TenantInvitations
            .Where(i => i.TenantId == tenantId && i.Status == InvitationStatuses.Pending)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

    public async Task<TenantInvitation?> GetPendingByEmailAsync(Guid tenantId, string email)
    {
        // InvitedEmail is stored normalized; normalize the input in C# and compare directly
        // (no per-row SQL ToLower(), which is culture-dependent and index-defeating).
        var normalized = email.ToLowerInvariant();
        return await db.TenantInvitations.FirstOrDefaultAsync(i =>
            i.TenantId == tenantId
            && i.Status == InvitationStatuses.Pending
            && i.InvitedEmail == normalized);
    }

    // Pre-membership lookup: the accepting user is not yet in the invitation's tenant
    // (their JWT still carries their old tenant), so this must bypass the tenant filter.
    public async Task<TenantInvitation?> GetByTokenHashAsync(string tokenHash) =>
        await db.TenantInvitations.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.TokenHash == tokenHash);

    public async Task<bool> TryAcceptAsync(Guid id)
    {
        // EF InMemory doesn't support ExecuteUpdateAsync; fall back to a tracked entity
        // update. Unit tests are single-threaded so there is no concurrency risk there.
        // IgnoreQueryFilters throughout: accept runs while the JWT still holds the
        // user's old tenant, so the global filter would otherwise hide the invitation.
        if (!db.Database.IsRelational())
        {
            var inv = await db.TenantInvitations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.Id == id && i.Status == InvitationStatuses.Pending);
            if (inv is null) return false;
            inv.Status = InvitationStatuses.Accepted;
            await db.SaveChangesAsync();
            return true;
        }

        // Relational path: single conditional UPDATE — atomic at the DB level so a
        // concurrent second accept finds 0 rows to update and loses the race.
        var affected = await db.TenantInvitations.IgnoreQueryFilters()
            .Where(i => i.Id == id && i.Status == InvitationStatuses.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvitationStatuses.Accepted));
        return affected == 1;
    }

    public async Task<TenantInvitation> UpdateAsync(TenantInvitation invitation)
    {
        db.TenantInvitations.Update(invitation);
        await db.SaveChangesAsync();
        return invitation;
    }
}
