namespace PakistanAccountingERP.Application.DTOs;

public record RoleListItemDto(string Id, string Name);

public record PermissionMatrixRowDto(
    int PermissionId,
    string Module,
    string Action,
    string Key,
    bool Allowed);

public record RolePermissionsDto(
    string RoleId,
    string RoleName,
    IReadOnlyList<PermissionMatrixRowDto> Permissions);

public class RolePermissionsUpdateRequest
{
    public List<RolePermissionUpdateItemDto> Permissions { get; set; } = [];
}

public class RolePermissionUpdateItemDto
{
    public int PermissionId { get; set; }
    public bool Allowed { get; set; }
}

public record RolePermissionsUpdateResult(bool Success, string? Message);
