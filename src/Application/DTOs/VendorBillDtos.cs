using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record VendorBillListItemDto(
    int Id,
    string BillNumber,
    string VendorName,
    DateTime BillDate,
    decimal NetAmount,
    string Status,
    bool CanEdit,
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
    int? WarehouseId,
    string? WarehouseCode,
    string? WarehouseName,
    decimal SubTotal,
    decimal TaxAmount,
    decimal WithholdingTaxRate,
    decimal WithholdingTaxAmount,
    decimal IncomeTax236GRate,
    decimal IncomeTax236GAmount,
    decimal GrossAmount,
    decimal NetAmount,
    decimal TotalQuantity,
    decimal TotalCartons,
    BillStatus Status,
    int? JournalEntryId,
    string? JournalEntryNumber,
    IReadOnlyList<VendorBillLineDto> Lines,
    IReadOnlyList<DocumentAttachmentDto> Attachments);

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
    public int? Id { get; set; }
    public string BillNumber { get; set; } = string.Empty;
    public int VendorId { get; set; }
    public int? WarehouseId { get; set; }
    public DateTime BillDate { get; set; }
    public string? RefNo { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal? WithholdingTaxRate { get; set; }
    public decimal? WithholdingTaxAmount { get; set; }
    public decimal? IncomeTax236GRate { get; set; }
    public decimal? IncomeTax236GAmount { get; set; }
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
    string? Description,
    string StackNo,
    string LotNo,
    decimal PurchaseRate);

public record VendorBillWarehouseLookupDto(int Id, string Code, string Name);

public record VendorBillPurchaseTaxSettingsDto(
    bool SupportsPurchaseWithholdingTax,
    decimal PurchaseWithholdingTaxRate,
    string WithholdingTaxSection,
    string WithholdingTaxSectionLabel,
    string NatureOfPayment,
    decimal DefaultIncomeTax236GRate,
    string IncomeTax236GSection,
    string IncomeTax236GSectionLabel);
