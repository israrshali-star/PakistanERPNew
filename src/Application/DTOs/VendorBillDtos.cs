using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record VendorBillListItemDto(
    int Id,
    string BillNumber,
    string VendorName,
    DateTime BillDate,
    decimal NetAmount,
    string Status,
    bool CanApprove,
    bool CanDelete,
    bool IsActive);

public record VendorBillLineDto(
    int Id,
    int? ItemId,
    string? ItemCode,
    string? ItemName,
    string? Description,
    string? StackNo,
    string? LotNo,
    decimal Quantity,
    decimal Cartons,
    decimal Rate,
    decimal Amount);

public record VendorBillDetailDto(
    int Id,
    string BillNumber,
    string? RefNo,
    int VendorId,
    string VendorCode,
    string VendorName,
    DateTime BillDate,
    decimal SubTotal,
    decimal TaxAmount,
    decimal NetAmount,
    decimal TotalQuantity,
    decimal TotalCartons,
    BillStatus Status,
    int? JournalEntryId,
    string? JournalEntryNumber,
    IReadOnlyList<VendorBillLineDto> Lines);

public class VendorBillLineSaveRequest
{
    public int? ItemId { get; set; }
    public string? Description { get; set; }
    public string? StackNo { get; set; }
    public string? LotNo { get; set; }
    public decimal Quantity { get; set; }
    public decimal Cartons { get; set; }
    public decimal Rate { get; set; }
}

public class VendorBillSaveRequest
{
    public string BillNumber { get; set; } = string.Empty;
    public int VendorId { get; set; }
    public DateTime BillDate { get; set; }
    public string? RefNo { get; set; }
    public decimal? TaxRate { get; set; }
    public List<VendorBillLineSaveRequest> Lines { get; set; } = new();
}

public record VendorBillSaveResult(bool Success, string? Message, int? BillId);

public record VendorBillActionResult(bool Success, string? Message, VendorBillDetailDto? Bill);

public record NextVendorBillNumberDto(string BillNumber);

public record VendorBillVendorLookupDto(
    int Id,
    string VendorCode,
    string VendorName,
    decimal DefaultTaxRate);

public record VendorBillItemLookupDto(
    int Id,
    string ItemCode,
    string ItemName,
    string StackNo,
    string LotNo,
    decimal PurchaseRate);
