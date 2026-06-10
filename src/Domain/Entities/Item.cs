namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Domain.Enums;

public class Item : CompanyAuditableEntity
{
    public int Id { get; set; }
    public ItemType ItemType { get; set; } = ItemType.Goods;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string StackNo { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? HSCode { get; set; }
    public string? Barcode { get; set; }
    public int UnitOfMeasureId { get; set; }
    public int? ItemCategoryId { get; set; }
    public decimal PurchaseRate { get; set; }
    public decimal SaleRate { get; set; }
    public decimal MinimumStock { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal Cartons { get; set; }
    public CostingMethod CostingMethod { get; set; } = CostingMethod.FIFO;
    public bool IsActive { get; set; } = true;

    public UnitOfMeasure UnitOfMeasure { get; set; } = null!;
    public ItemCategory? ItemCategory { get; set; }
    public Company Company { get; set; } = null!;
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
    public ICollection<SalesInvoiceLine> SalesInvoiceLines { get; set; } = new List<SalesInvoiceLine>();
    public ICollection<VendorBillLine> VendorBillLines { get; set; } = new List<VendorBillLine>();
}
