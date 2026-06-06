namespace PakistanAccountingERP.Domain.Entities;

public class RolePermission
{
    public int Id { get; set; }
    public string RoleId { get; set; } = string.Empty;
    public int PermissionId { get; set; }
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }

    public Permission Permission { get; set; } = null!;
}
