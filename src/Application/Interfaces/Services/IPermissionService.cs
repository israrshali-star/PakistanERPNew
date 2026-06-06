namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Checks role-based permissions (e.g. "Sales.Create").
/// </summary>
public interface IPermissionService
{
    Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetUserPermissionKeysAsync(CancellationToken cancellationToken = default);
    Task InvalidateCacheAsync(string? userId = null, CancellationToken cancellationToken = default);
}
