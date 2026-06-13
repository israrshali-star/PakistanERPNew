namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Domain.Enums;

public class CustomerReceipt : CompanyAuditableEntity
{
    public int Id { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public DateTime ReceiptDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public ChequeBankType? ChequeBankType { get; set; }
    public int? BankId { get; set; }
    public string? ChequeNumber { get; set; }
    public DateTime? ChequeDate { get; set; }
    public string? Notes { get; set; }
    public CustomerReceiptStatus Status { get; set; } = CustomerReceiptStatus.InClearing;
    public DateTime? ClearedAt { get; set; }
    public string? ClearedBy { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public string? ReturnedBy { get; set; }
    public bool IsDeposited { get; set; }
    public int? DepositedBankTransactionId { get; set; }

    public Customer Customer { get; set; } = null!;
    public Bank? Bank { get; set; }
    public BankTransaction? DepositedBankTransaction { get; set; }
    public Company Company { get; set; } = null!;
}
