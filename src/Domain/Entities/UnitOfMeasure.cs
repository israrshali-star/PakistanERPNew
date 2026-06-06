namespace PakistanAccountingERP.Domain.Entities;

public class UnitOfMeasure
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Symbol { get; set; }

    public ICollection<Item> Items { get; set; } = new List<Item>();
}
