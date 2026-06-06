namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Domain.Enums;

public class InventoryTransaction : CompanyAuditableEntity
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int WarehouseId { get; set; }
    public InventoryTransactionType TransactionType { get; set; }
    public string? StackNo { get; set; }
    public string? LotNo { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Notes { get; set; }

    public Item Item { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public Company Company { get; set; } = null!;
}
