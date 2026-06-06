namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;

public class TaxSetting : CompanyAuditableEntity
{
    public int Id { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal SalesTaxRate { get; set; } = 18m;
    public decimal UnregisteredSalesTaxRate { get; set; } = 18m;
    public bool IsActive { get; set; } = true;

    public Company Company { get; set; } = null!;
}
