namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;

public class Vendor : CompanyAuditableEntity
{
    public int Id { get; set; }
    public string VendorCode { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public string? Address { get; set; }
    public int? ProvinceId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? NTN { get; set; }
    public decimal DefaultSalesTaxRate { get; set; } = 18m;
    public bool IsActive { get; set; } = true;

    public Province? Province { get; set; }
    public Company Company { get; set; } = null!;
    public ICollection<VendorBill> VendorBills { get; set; } = new List<VendorBill>();
    public ICollection<VendorPayment> VendorPayments { get; set; } = new List<VendorPayment>();
    public ICollection<BankTransaction> WriteChequePayments { get; set; } = new List<BankTransaction>();
}
