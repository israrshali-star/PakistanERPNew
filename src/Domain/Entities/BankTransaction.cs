namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Domain.Enums;

public class BankTransaction : CompanyAuditableEntity
{
    public int Id { get; set; }
    public int BankId { get; set; }
    public int ChartOfAccountId { get; set; }
    public BankTransactionType TransactionType { get; set; }
    public int? TransferToBankId { get; set; }
    public int? TransferToChartOfAccountId { get; set; }
    public int? CounterChartOfAccountId { get; set; }
    public int? CustomerId { get; set; }
    public int? VendorId { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public int? JournalEntryId { get; set; }
    public string? PartyName { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? ChequeNumber { get; set; }
    public DateTime? ChequeDate { get; set; }
    public decimal Amount { get; set; }
    public decimal CustomerBalanceEffect { get; set; }
    public string? Description { get; set; }
    public bool IsReconciled { get; set; }

    public Bank Bank { get; set; } = null!;
    public ChartOfAccount ChartOfAccount { get; set; } = null!;
    public ChartOfAccount? TransferToChartOfAccount { get; set; }
    public ChartOfAccount? CounterChartOfAccount { get; set; }
    public Customer? Customer { get; set; }
    public Vendor? Vendor { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public Bank? TransferToBank { get; set; }
    public Company Company { get; set; } = null!;
    public ICollection<CustomerReceipt> DepositedCustomerReceipts { get; set; } = [];
}
