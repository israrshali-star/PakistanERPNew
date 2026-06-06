namespace PakistanAccountingERP.Domain.Common;

public abstract class CompanyAuditableEntity : AuditableEntity
{
    public int CompanyId { get; set; }
}
