namespace PakistanAccountingERP.Domain.Entities;

public class Permission
{
    public int Id { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
