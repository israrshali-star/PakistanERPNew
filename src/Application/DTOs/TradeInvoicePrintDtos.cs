namespace PakistanAccountingERP.Application.DTOs;

public record TradeInvoicePrintDto(
    string InvoiceNumber,
    DateTime InvoiceDate,
    string SellerName,
    string CustomerName,
    decimal CustomerTotalBalance,
    decimal TaxableTotal,
    decimal TaxAmount,
    decimal TaxRateDisplay,
    decimal NetTotal,
    DateTime PrintedAt,
    IReadOnlyList<TradeInvoicePrintLineDto> Lines);

public record TradeInvoicePrintLineDto(
    string Description,
    string? CartonDescription,
    decimal Cartons,
    decimal Quantity,
    decimal Rate,
    decimal Amount);
