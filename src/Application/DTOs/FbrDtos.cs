namespace PakistanAccountingERP.Application.DTOs;

public record FbrSubmissionRequest(
    int SalesInvoiceId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string? SellerNtn,
    string? BuyerNtn,
    string? BuyerCnic,
    string? BuyerName,
    int ScenarioId,
    string ScenarioCode,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal NetTotal,
    IReadOnlyList<FbrSubmissionLineRequest> Lines);

public record FbrSubmissionLineRequest(
    string? HsCode,
    string ProductDescription,
    string? Unit,
    decimal Quantity,
    decimal Price,
    decimal TaxRate,
    decimal TaxAmount,
    decimal LineTotal);

public record FbrSubmissionResult(
    bool Success,
    string? Message,
    string? FbrInvoiceNumber,
    string? ResponseJson,
    bool IsSimulation);
