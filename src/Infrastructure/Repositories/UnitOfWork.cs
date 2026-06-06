using Microsoft.EntityFrameworkCore.Storage;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Infrastructure.Data;

namespace PakistanAccountingERP.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private readonly Dictionary<Type, object> _repositories = new();
    private IDbContextTransaction? _transaction;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public IRepository<TEntity> Repository<TEntity>() where TEntity : class
    {
        var type = typeof(TEntity);

        if (!_repositories.TryGetValue(type, out var repository))
        {
            repository = new Repository<TEntity>(_context);
            _repositories[type] = repository;
        }

        return (IRepository<TEntity>)repository;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already in progress.");
        }

        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("No active transaction to commit.");
        }

        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("No active transaction to roll back.");
        }

        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
