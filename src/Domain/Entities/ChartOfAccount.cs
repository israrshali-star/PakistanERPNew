namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;

public class ChartOfAccount : CompanyAuditableEntity
{
    public int Id { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public int? TypeId { get; set; }
    public int? SubTypeId { get; set; }
    public int? ParentAccountId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal OpeningBalance { get; set; }

    public AccountType? AccountType { get; set; }
    public SubAccountType? SubAccountType { get; set; }
    public ChartOfAccount? ParentAccount { get; set; }
    public ICollection<ChartOfAccount> ChildAccounts { get; set; } = new List<ChartOfAccount>();
    public Company Company { get; set; } = null!;
    public ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();
    public ICollection<Bank> Banks { get; set; } = new List<Bank>();
}
