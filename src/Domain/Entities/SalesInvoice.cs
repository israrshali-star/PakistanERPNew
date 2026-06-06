namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Domain.Enums;

public class SalesInvoice : CompanyAuditableEntity
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? BuyerAddress { get; set; }
    public int? ProvinceId { get; set; }
    public string? BuyerNTN { get; set; }
    public string? BuyerCNIC { get; set; }
    public DateTime InvoiceDate { get; set; }
    public InvoiceType InvoiceType { get; set; } = InvoiceType.SalesInvoice;
    public int? ScenarioId { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal FurtherTax { get; set; }
    public decimal FED { get; set; }
    public decimal ExtraTax { get; set; }
    public decimal WithholdingTax { get; set; }
    public decimal NetTotal { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public int? JournalEntryId { get; set; }
    public string? FbrInvoiceNumber { get; set; }
    public string? FbrResponseJson { get; set; }
    public DateTime? FbrSubmittedAt { get; set; }

    public Customer Customer { get; set; } = null!;
    public Province? Province { get; set; }
    public ScenarioType? ScenarioType { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public Company Company { get; set; } = null!;
    public ICollection<SalesInvoiceLine> Lines { get; set; } = new List<SalesInvoiceLine>();
}
