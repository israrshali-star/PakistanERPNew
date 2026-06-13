namespace PakistanAccountingERP.Domain.Entities;

public class SalesInvoiceLine
{
    public int Id { get; set; }
    public int SalesInvoiceId { get; set; }
    public int ItemId { get; set; }
    public string? HSCode { get; set; }
    public string? CartonDescription { get; set; }
    public string? ProductDescription { get; set; }
    public string? Unit { get; set; }
    public string? StackNo { get; set; }
    public string? LotNo { get; set; }
    public decimal Quantity { get; set; }
    public decimal Cartons { get; set; }
    public decimal Price { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Discount { get; set; }
    public decimal LineTotal { get; set; }

    public SalesInvoice SalesInvoice { get; set; } = null!;
    public Item Item { get; set; } = null!;
}
