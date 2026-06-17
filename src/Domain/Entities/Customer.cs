namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Domain.Enums;

public class Customer : CompanyAuditableEntity
{
    public int Id { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public string? Address { get; set; }
    public int? ProvinceId { get; set; }
    public int ScenarioId { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? NTN { get; set; }
    public string? CNIC { get; set; }
    public string? STRN { get; set; }
    public CustomerType CustomerType { get; set; } = CustomerType.Registered;
    public InvoiceType InvoiceType { get; set; } = InvoiceType.SalesInvoice;
    /// <summary>When set, overrides company default further tax % (e.g. 2 for reduced-rate customers).</summary>
    public decimal? FurtherTaxRate { get; set; }
    public bool IsActive { get; set; } = true;

    public Province? Province { get; set; }
    public ScenarioType ScenarioType { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public ICollection<SalesInvoice> SalesInvoices { get; set; } = new List<SalesInvoice>();
    public ICollection<CustomerReceipt> CustomerReceipts { get; set; } = new List<CustomerReceipt>();
    public ICollection<BankTransaction> WriteChequePayments { get; set; } = new List<BankTransaction>();
}
