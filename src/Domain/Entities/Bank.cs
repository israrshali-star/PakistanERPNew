namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;

public class Bank : CompanyAuditableEntity
{
    public int Id { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string AccountTitle { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string? IBAN { get; set; }
    public int? ChartOfAccountId { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    /// <summary>Next cheque number to suggest when writing a cheque from this bank.</summary>
    public string? NextChequeNumber { get; set; }
    public bool IsActive { get; set; } = true;

    public ChartOfAccount? ChartOfAccount { get; set; }
    public Company Company { get; set; } = null!;
    public ICollection<BankTransaction> BankTransactions { get; set; } = new List<BankTransaction>();
    public ICollection<BankTransaction> TransferToTransactions { get; set; } = new List<BankTransaction>();
    public ICollection<BankReconciliation> BankReconciliations { get; set; } = new List<BankReconciliation>();
}
