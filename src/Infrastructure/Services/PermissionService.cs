using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Infrastructure.Data;

namespace PakistanAccountingERP.Infrastructure.Services;

public class PermissionService : IPermissionService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public PermissionService(
        AppDbContext context,
        ICurrentUserService currentUser,
        RoleManager<IdentityRole> roleManager,
        IMemoryCache cache)
    {
        _context = context;
        _currentUser = currentUser;
        _roleManager = roleManager;
        _cache = cache;
    }

    public async Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default)
    {
        if (IsSuperAdmin())
        {
            return true;
        }

        var keys = await GetUserPermissionKeysAsync(cancellationToken);
        return keys.Contains(permissionKey, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<string>> GetUserPermissionKeysAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Array.Empty<string>();
        }

        if (IsSuperAdmin())
        {
            return await GetAllPermissionKeysAsync(cancellationToken);
        }

        var cacheKey = $"permissions:{_currentUser.UserId}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached is not null)
        {
            return cached;
        }

        var roleIds = new List<string>();
        foreach (var roleName in _currentUser.Roles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is not null)
            {
                roleIds.Add(role.Id);
            }
        }

        if (roleIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        var keys = await _context.RolePermissions
            .AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Join(
                _context.Permissions.AsNoTracking(),
                rp => rp.PermissionId,
                p => p.Id,
                (rp, p) => new { rp, p })
            .Where(x =>
                (x.p.Action == "View" && x.rp.CanView) ||
                (x.p.Action == "Create" && x.rp.CanCreate) ||
                (x.p.Action == "Edit" && x.rp.CanEdit) ||
                (x.p.Action == "Delete" && x.rp.CanDelete))
            .Select(x => x.p.Key)
            .Distinct()
            .ToListAsync(cancellationToken);

        _cache.Set(cacheKey, keys, CacheDuration);
        return keys;
    }

    public Task InvalidateCacheAsync(string? userId = null, CancellationToken cancellationToken = default)
    {
        var key = $"permissions:{userId ?? _currentUser.UserId}";
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    private bool IsSuperAdmin() =>
        _currentUser.Roles.Any(r => string.Equals(r, "SuperAdmin", StringComparison.OrdinalIgnoreCase));

    private async Task<IReadOnlyList<string>> GetAllPermissionKeysAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "permissions:all-keys";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached is not null)
        {
            return cached;
        }

        var keys = await _context.Permissions
            .AsNoTracking()
            .Select(p => p.Key)
            .ToListAsync(cancellationToken);

        _cache.Set(cacheKey, keys, CacheDuration);
        return keys;
    }
}
