namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Domain.Enums;

public class JournalEntry : CompanyAuditableEntity
{
    public int Id { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; }
    public string? Description { get; set; }
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public JournalStatus Status { get; set; } = JournalStatus.Draft;

    public Company Company { get; set; } = null!;
    public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
    public ICollection<SalesInvoice> SalesInvoices { get; set; } = new List<SalesInvoice>();
    public ICollection<VendorBill> VendorBills { get; set; } = new List<VendorBill>();
}
