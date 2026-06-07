using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record SalesInvoicePrintDto(
    string InvoiceNumber,
    string? FbrInvoiceNumber,
    DateTime InvoiceDate,
    InvoiceType InvoiceType,
    string? ScenarioCode,
    string TaxPeriod,
    string InvoiceTypeLabel,
    SalesInvoicePrintPartyDto Seller,
    SalesInvoicePrintPartyDto Buyer,
    decimal ExclusiveTotal,
    decimal SalesTaxTotal,
    decimal InclusiveTotal,
    string AmountInWords,
    DateTime PrintedAt,
    IReadOnlyList<SalesInvoicePrintLineDto> Lines,
    string FooterNotice);

public record SalesInvoicePrintPartyDto(
    string Name,
    string? Ntn,
    string? Cnic,
    string? Address,
    string? Province,
    string? Phone,
    string? Email,
    string? BuyerId = null);

public record SalesInvoicePrintLineDto(
    int LineNo,
    string ProductDisplay,
    string? HsCode,
    string SaleType,
    decimal Quantity,
    string? Unit,
    string TaxRateDisplay,
    decimal ValueExcludingSt,
    decimal SalesTax,
    decimal FurtherTax,
    decimal LineTotal);
