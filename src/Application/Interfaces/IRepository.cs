namespace PakistanAccountingERP.Application.Interfaces;

/// <summary>
/// Generic read/write repository for EF Core entities.
/// </summary>
public interface IRepository<TEntity> where TEntity : class
{
    /// <summary>Gets an entity by primary key.</summary>
    Task<TEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns a queryable for filtering. Uses AsNoTracking when <paramref name="asNoTracking"/> is true.</summary>
    IQueryable<TEntity> Query(bool asNoTracking = true);

    /// <summary>Adds a new entity to the change tracker.</summary>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Adds multiple entities to the change tracker.</summary>
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>Marks an entity as modified.</summary>
    void Update(TEntity entity);

    /// <summary>Soft-deletes when supported; otherwise removes from the database.</summary>
    void Remove(TEntity entity);

    /// <summary>Soft-deletes or removes multiple entities.</summary>
    void RemoveRange(IEnumerable<TEntity> entities);
}
