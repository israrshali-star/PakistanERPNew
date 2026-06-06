namespace PakistanAccountingERP.Application.DTOs;

public record SalesRegisterLineDto(
    int InvoiceId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string CustomerName,
    string InvoiceType,
    string Status,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal NetTotal,
    string? FbrInvoiceNumber);

public record SalesRegisterReportDto(
    DateTime FromDate,
    DateTime ToDate,
    int? CustomerId,
    string? CustomerLabel,
    int InvoiceCount,
    decimal TotalSubTotal,
    decimal TotalDiscount,
    decimal TotalTax,
    decimal TotalNet,
    IReadOnlyList<SalesRegisterLineDto> Lines);

public record SalesByCustomerLineDto(
    int CustomerId,
    string CustomerCode,
    string CustomerName,
    int InvoiceCount,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal NetTotal);

public record SalesByCustomerReportDto(
    DateTime FromDate,
    DateTime ToDate,
    int CustomerCount,
    decimal TotalSubTotal,
    decimal TotalDiscount,
    decimal TotalTax,
    decimal TotalNet,
    IReadOnlyList<SalesByCustomerLineDto> Lines);

public record SalesTaxSummaryReportDto(
    DateTime FromDate,
    DateTime ToDate,
    int InvoiceCount,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal FurtherTax,
    decimal Fed,
    decimal ExtraTax,
    decimal WithholdingTax,
    decimal NetTotal);

public record SalesReportCustomerLookupDto(int Id, string BuyerId, string Name);

public class SalesReportRequest
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int? CustomerId { get; set; }
    public bool PostedOnly { get; set; } = true;
}
