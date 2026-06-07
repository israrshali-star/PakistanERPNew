using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IRolePermissionManagementService
{
    Task<IReadOnlyList<RoleListItemDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<RolePermissionsDto?> GetRolePermissionsAsync(string roleId, CancellationToken cancellationToken = default);

    Task<RolePermissionsUpdateResult> UpdateRolePermissionsAsync(
        string roleId,
        RolePermissionsUpdateRequest request,
        CancellationToken cancellationToken = default);
}
