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
