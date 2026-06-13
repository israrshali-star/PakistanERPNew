namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Domain.Enums;

public class VendorBill : CompanyAuditableEntity
{
    public int Id { get; set; }
    public int VendorId { get; set; }
    public int? WarehouseId { get; set; }
    public string BillNumber { get; set; } = string.Empty;
    public string? RefNo { get; set; }
    public DateTime BillDate { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalCartons { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal NetAmount { get; set; }
    public BillStatus Status { get; set; } = BillStatus.Draft;
    public int? JournalEntryId { get; set; }

    public Vendor Vendor { get; set; } = null!;
    public Warehouse? Warehouse { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public Company Company { get; set; } = null!;
    public ICollection<VendorBillLine> Lines { get; set; } = new List<VendorBillLine>();
    public ICollection<VendorBillAttachment> Attachments { get; set; } = new List<VendorBillAttachment>();
}
