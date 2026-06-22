using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Template.Core.Entities;

namespace Template.Infrastructure.Persistence;

/// <summary>
/// Write-side half of tenant isolation. The global query filter scopes <em>reads</em>; this
/// interceptor scopes <em>writes</em>, so a feature slice can no more persist a row under the
/// wrong tenant than it can read one. For every <c>Added</c> entity implementing
/// <see cref="ITenantScoped"/> while a tenant is current:
/// <list type="bullet">
///   <item>unset <c>TenantId</c> (default) → stamped with the current tenant; and</item>
///   <item>a <c>TenantId</c> belonging to a <em>different</em> tenant → throws (fail closed),
///   so the change never reaches the database.</item>
/// </list>
/// <para>
/// When there is no current tenant (<see cref="AppDbContext.CurrentTenantId"/> is
/// <see cref="Guid.Empty"/>) the context is a system/seed/cross-tenant one — the same trust
/// level that may call <c>IgnoreQueryFilters()</c> — so no stamping or validation is applied.
/// The interceptor is stateless; it reads the tenant from the context being saved, so a single
/// instance is safe to share across every <see cref="AppDbContext"/>.
/// </para>
/// </summary>
public sealed class TenantStampingInterceptor : SaveChangesInterceptor
{
    public static readonly TenantStampingInterceptor Instance = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context as AppDbContext);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context as AppDbContext);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Stamp(AppDbContext? db)
    {
        if (db is null) return;

        var currentTenantId = db.CurrentTenantId;
        if (currentTenantId == Guid.Empty) return; // system/seed/cross-tenant context — not enforced

        foreach (var entry in db.ChangeTracker.Entries<ITenantScoped>())
        {
            if (entry.State != EntityState.Added) continue;

            var tenantProperty = entry.Property(nameof(ITenantScoped.TenantId));
            var tenantId = (Guid)tenantProperty.CurrentValue!;

            if (tenantId == Guid.Empty)
            {
                tenantProperty.CurrentValue = currentTenantId; // stamp the owning tenant
            }
            else if (tenantId != currentTenantId)
            {
                throw new InvalidOperationException(
                    $"Refusing to persist a {entry.Entity.GetType().Name} for tenant {tenantId} " +
                    $"while the current tenant is {currentTenantId}. A tenant-scoped entity may only " +
                    "be written under its owning tenant; cross-tenant writes must run on a system " +
                    "context (no current tenant).");
            }
        }
    }
}
