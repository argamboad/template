namespace Template.Core.Repositories;

/// <summary>
/// A thin generic repository for FEATURE/domain entities, so a vertical slice can read and
/// write its own data without authoring a bespoke repository pair. Reads via
/// <see cref="Query"/> are automatically tenant-scoped for <c>ITenantScoped</c> entities
/// (the AppDbContext global query filter), so a slice can't forget to scope.
/// <para>
/// Platform/auth entities (User, Tenant, tokens, …) keep their dedicated repositories;
/// use this only for app/domain tables.
/// </para>
/// </summary>
public interface IRepository<TEntity> where TEntity : class
{
    /// <summary>
    /// Queryable over the set — tenant-filtered for <c>ITenantScoped</c> entities. Compose
    /// typed LINQ in the slice, e.g. <c>Query().Where(...).ToListAsync(ct)</c>.
    /// </summary>
    IQueryable<TEntity> Query();

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    void Update(TEntity entity);
    void Remove(TEntity entity);

    /// <summary>Persists pending changes; returns the affected row count.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
