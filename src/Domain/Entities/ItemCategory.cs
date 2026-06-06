namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;

public class ItemCategory : CompanyAuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Company Company { get; set; } = null!;
    public ICollection<Item> Items { get; set; } = new List<Item>();
}
