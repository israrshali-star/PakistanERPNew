namespace PakistanAccountingERP.Application.DTOs;

public record PurchaseRegisterLineDto(
    int BillId,
    string BillNumber,
    string? RefNo,
    DateTime BillDate,
    string VendorName,
    string Status,
    decimal TotalQuantity,
    decimal TaxAmount,
    decimal NetAmount);

public record PurchaseRegisterReportDto(
    DateTime FromDate,
    DateTime ToDate,
    int? VendorId,
    string? VendorLabel,
    int BillCount,
    decimal TotalQuantity,
    decimal TotalTax,
    decimal TotalNet,
    IReadOnlyList<PurchaseRegisterLineDto> Lines);

public record PurchaseByVendorLineDto(
    int VendorId,
    string VendorCode,
    string VendorName,
    int BillCount,
    decimal TotalQuantity,
    decimal TaxAmount,
    decimal NetAmount);

public record PurchaseByVendorReportDto(
    DateTime FromDate,
    DateTime ToDate,
    int VendorCount,
    decimal TotalQuantity,
    decimal TotalTax,
    decimal TotalNet,
    IReadOnlyList<PurchaseByVendorLineDto> Lines);

public record InputTaxSummaryReportDto(
    DateTime FromDate,
    DateTime ToDate,
    int BillCount,
    decimal TotalQuantity,
    decimal InputTaxAmount,
    decimal NetAmount);

public record PurchaseReportVendorLookupDto(int Id, string VendorCode, string Name);

public class PurchaseReportRequest
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int? VendorId { get; set; }
    public bool ApprovedOnly { get; set; } = true;
}

public record StackLotMovementDto(
    string MovementType,
    string ReferenceNumber,
    DateTime Date,
    decimal Cartons,
    decimal Weight,
    decimal Amount);

public record StackLotTrackingLineDto(
    int ItemId,
    string ItemCode,
    string ItemName,
    string? StackNo,
    string? LotNo,
    decimal PurchasedCartons,
    decimal PurchasedWeight,
    decimal PurchasedAmount,
    decimal SoldCartons,
    decimal SoldWeight,
    decimal SoldAmount,
    decimal RemainingCartons,
    decimal RemainingWeight,
    IReadOnlyList<StackLotMovementDto> Movements);

public record StackLotTrackingReportDto(
    int? ItemId,
    string? ItemLabel,
    string? LotNo,
    string? StackNo,
    decimal TotalPurchasedCartons,
    decimal TotalPurchasedWeight,
    decimal TotalPurchasedAmount,
    decimal TotalSoldCartons,
    decimal TotalSoldWeight,
    decimal TotalSoldAmount,
    decimal TotalRemainingCartons,
    decimal TotalRemainingWeight,
    IReadOnlyList<StackLotTrackingLineDto> Lines);

public record StackLotReportItemLookupDto(int Id, string ItemCode, string Name);

public record StackLotFilterLookupDto(
    IReadOnlyList<string> StackNos,
    IReadOnlyList<string> LotNos);

public class StackLotTrackingRequest
{
    public int? ItemId { get; set; }
    public string? LotNo { get; set; }
    public string? StackNo { get; set; }
}
