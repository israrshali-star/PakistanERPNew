using PakistanAccountingERP.Domain.Common;

namespace PakistanAccountingERP.Domain.Entities;

public class FiscalYear : CompanyAuditableEntity
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsClosed { get; set; }

    public Company Company { get; set; } = null!;
}
