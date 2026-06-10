namespace PakistanAccountingERP.Application.Interfaces;

/// <summary>
/// Coordinates repositories and persists changes through a single DbContext.
/// </summary>
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    /// <summary>Gets a repository for the given entity type (cached per unit-of-work instance).</summary>
    IRepository<TEntity> Repository<TEntity>() where TEntity : class;

    /// <summary>Persists all pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Begins a database transaction.</summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits the current transaction.</summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Rolls back the current transaction.</summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
