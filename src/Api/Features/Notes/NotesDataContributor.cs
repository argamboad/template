using Microsoft.EntityFrameworkCore;
using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Core.Repositories;

namespace Template.Api.Features.Notes;

/// <summary>
/// 🗑️ DELETE-ME: sample feature's tenant-data hook. Registering this (in Program.cs) is
/// all it takes for the dissolve flow to account for and wipe this feature's data — no
/// edits to any central wipe method. Every real tenant-scoped feature ships one of these.
/// </summary>
public class NotesDataContributor(IRepository<Note> notes) : ITenantDataContributor
{
    public Task<bool> HasDataAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        // QueryAllTenants: dissolve runs for a tenant other than the current one, so this
        // crosses tenants by design — the audited escape hatch, re-constrained to the target.
        notes.QueryAllTenants().AnyAsync(n => n.TenantId == tenantId, cancellationToken);

    public async Task WipeAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await notes.QueryAllTenants()
            .Where(n => n.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);
}
