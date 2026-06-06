namespace PakistanAccountingERP.Domain.Entities;

/// <summary>
/// FBR sales scenario lookup (SN001, SN002, etc.).
/// </summary>
public class ScenarioType
{
    public int ScenarioId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<Customer> Customers { get; set; } = new List<Customer>();
    public ICollection<SalesInvoice> SalesInvoices { get; set; } = new List<SalesInvoice>();
}
