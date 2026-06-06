namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;

public class BankReconciliation : CompanyAuditableEntity
{
    public int Id { get; set; }
    public int BankId { get; set; }
    public DateTime StatementDate { get; set; }
    public decimal StatementBalance { get; set; }
    public decimal BookBalance { get; set; }
    public bool IsCompleted { get; set; }

    public Bank Bank { get; set; } = null!;
    public Company Company { get; set; } = null!;
}
