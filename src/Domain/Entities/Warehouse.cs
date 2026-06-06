namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;

public class Warehouse : CompanyAuditableEntity
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;

    public Company Company { get; set; } = null!;
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
}
