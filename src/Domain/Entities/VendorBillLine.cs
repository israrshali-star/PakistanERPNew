namespace PakistanAccountingERP.Domain.Entities;

public class VendorBillLine
{
    public int Id { get; set; }
    public int VendorBillId { get; set; }
    public int? ItemId { get; set; }
    public string? Description { get; set; }
    public string? StackNo { get; set; }
    public string? LotNo { get; set; }
    public decimal Quantity { get; set; }
    public decimal Cartons { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }

    public VendorBill VendorBill { get; set; } = null!;
    public Item? Item { get; set; }
}
