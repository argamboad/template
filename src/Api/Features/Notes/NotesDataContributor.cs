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
        // IgnoreQueryFilters: the check runs for a tenant other than the current one.
        notes.Query().IgnoreQueryFilters().AnyAsync(n => n.TenantId == tenantId, cancellationToken);

    public async Task WipeAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await notes.Query().IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);
}
