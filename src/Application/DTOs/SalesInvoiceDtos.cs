using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record SalesInvoiceListItemDto(
    int Id,
    string InvoiceNumber,
    string CustomerName,
    DateTime InvoiceDate,
    decimal NetTotal,
    string Status,
    string? FbrInvoiceNumber,
    bool CanPost,
    bool CanSubmitFbr,
    bool IsActive);

public record SalesInvoiceLineDto(
    int Id,
    string ItemCode,
    string ItemName,
    string? HsCode,
    string? ProductDescription,
    string? Unit,
    string? StackNo,
    string? LotNo,
    decimal Quantity,
    decimal Cartons,
    decimal Price,
    decimal TaxRate,
    decimal TaxAmount,
    decimal Discount,
    decimal LineTotal);

public record SalesInvoiceDetailDto(
    int Id,
    string InvoiceNumber,
    int CustomerId,
    string CustomerName,
    string CustomerCode,
    DateTime InvoiceDate,
    InvoiceType InvoiceType,
    int? ScenarioId,
    string? ScenarioCode,
    string? BuyerAddress,
    string? BuyerNTN,
    string? BuyerCNIC,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal NetTotal,
    InvoiceStatus Status,
    string? FbrInvoiceNumber,
    DateTime? FbrSubmittedAt,
    int? JournalEntryId,
    string? JournalEntryNumber,
    IReadOnlyList<SalesInvoiceLineDto> Lines);

public record SalesInvoiceActionResult(bool Success, string? Message, SalesInvoiceDetailDto? Invoice);

public record SalesInvoiceCustomerLookupDto(
    int Id,
    string BuyerId,
    string BuyerName,
    int ScenarioId,
    int? ProvinceId,
    string? Address,
    string? NTN,
    string? CNIC,
    InvoiceType InvoiceType);

public record SalesInvoiceItemLookupDto(
    int Id,
    string ItemCode,
    string ItemName,
    string? Description,
    string? HSCode,
    string StackNo,
    string LotNo,
    string UnitSymbol,
    decimal SaleRate,
    decimal DefaultTaxRate);

public record NextInvoiceNumberDto(string InvoiceNumber);

public class SalesInvoiceLineSaveRequest
{
    public int ItemId { get; set; }
    public string? StackNo { get; set; }
    public string? LotNo { get; set; }
    public decimal Cartons { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal TaxRate { get; set; }
    public decimal Discount { get; set; }
}

public class SalesInvoiceSaveRequest
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public DateTime InvoiceDate { get; set; }
    public InvoiceType InvoiceType { get; set; } = InvoiceType.SalesInvoice;
    public int? ScenarioId { get; set; }
    public int? ProvinceId { get; set; }
    public string? BuyerAddress { get; set; }
    public string? BuyerNTN { get; set; }
    public string? BuyerCNIC { get; set; }
    public List<SalesInvoiceLineSaveRequest> Lines { get; set; } = new();
}

public record SalesInvoiceSaveResult(bool Success, string? Message, int? InvoiceId);
