using Microsoft.EntityFrameworkCore;
using Template.Core.Repositories;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of the generic feature repository. Registered open-generically
/// (<c>IRepository&lt;&gt; → EfRepository&lt;&gt;</c>) so any entity type resolves without
/// per-entity wiring.
/// </summary>
public class EfRepository<TEntity>(AppDbContext db) : IRepository<TEntity> where TEntity : class
{
    public IQueryable<TEntity> Query() => db.Set<TEntity>();

    public IQueryable<TEntity> QueryAllTenants() => db.Set<TEntity>().IgnoreQueryFilters();

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        await db.Set<TEntity>().AddAsync(entity, cancellationToken);

    public void Update(TEntity entity) => db.Set<TEntity>().Update(entity);

    public void Remove(TEntity entity) => db.Set<TEntity>().Remove(entity);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
