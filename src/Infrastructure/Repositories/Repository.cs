using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Infrastructure.Data;

namespace PakistanAccountingERP.Infrastructure.Repositories;

public class Repository<TEntity> : IRepository<TEntity> where TEntity : class
{
    private readonly AppDbContext _context;
    private readonly DbSet<TEntity> _dbSet;

    public Repository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<TEntity>();
    }

    public async Task<TEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync([id], cancellationToken);
    }

    public IQueryable<TEntity> Query(bool asNoTracking = true)
    {
        return asNoTracking ? _dbSet.AsNoTracking() : _dbSet;
    }

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(entity, cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddRangeAsync(entities, cancellationToken);
    }

    public void Update(TEntity entity)
    {
        _dbSet.Update(entity);
    }

    public void Remove(TEntity entity)
    {
        if (entity is ISoftDelete softDelete)
        {
            softDelete.IsDeleted = true;
            softDelete.DeletedAt = DateTime.UtcNow;
            _dbSet.Update(entity);
            return;
        }

        _dbSet.Remove(entity);
    }

    public void RemoveRange(IEnumerable<TEntity> entities)
    {
        foreach (var entity in entities)
        {
            Remove(entity);
        }
    }
}
