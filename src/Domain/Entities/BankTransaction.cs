namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Domain.Enums;

public class BankTransaction : CompanyAuditableEntity
{
    public int Id { get; set; }
    public int BankId { get; set; }
    public BankTransactionType TransactionType { get; set; }
    public int? TransferToBankId { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? ChequeNumber { get; set; }
    public DateTime? ChequeDate { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public bool IsReconciled { get; set; }

    public Bank Bank { get; set; } = null!;
    public Bank? TransferToBank { get; set; }
    public Company Company { get; set; } = null!;
}
