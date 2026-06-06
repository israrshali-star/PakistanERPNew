namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Domain.Enums;

public class VendorPayment : CompanyAuditableEntity
{
    public int Id { get; set; }
    public string PaymentNumber { get; set; } = string.Empty;
    public int VendorId { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public int? BankId { get; set; }
    public string? ChequeNumber { get; set; }
    public DateTime? ChequeDate { get; set; }
    public string? Notes { get; set; }

    public Vendor Vendor { get; set; } = null!;
    public Bank? Bank { get; set; }
    public Company Company { get; set; } = null!;
}
