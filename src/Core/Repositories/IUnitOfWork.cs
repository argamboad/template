namespace Template.Core.Repositories;

/// <summary>
/// A transactional boundary spanning several repository writes.
/// On a relational provider this is a real database transaction; on a
/// non-relational provider (EF InMemory in tests) it degrades to a no-op so the
/// exact same service code path runs in both. Begin → do work → CommitAsync;
/// disposing without committing rolls back (relational only).
/// </summary>
public interface IUnitOfWork
{
    Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
