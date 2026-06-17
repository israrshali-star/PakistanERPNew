using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record FbrPartyDto(
    string Name,
    string? Ntn,
    string? Cnic,
    string? Address,
    string? Province,
    string? Phone,
    string? Email,
    string? BuyerId = null);

public record FbrSubmissionRequest(
    int SalesInvoiceId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    InvoiceType InvoiceType,
    FbrPartyDto Seller,
    FbrPartyDto Buyer,
    string BuyerRegistrationType,
    string ScenarioCode,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal NetTotal,
    IReadOnlyList<FbrSubmissionLineRequest> Lines);

public record FbrSubmissionLineRequest(
    string? ItemCode,
    string? HsCode,
    string ProductDescription,
    string? Unit,
    string? StackNo,
    string? LotNo,
    decimal Quantity,
    decimal Cartons,
    decimal Price,
    decimal TaxRate,
    decimal SalesTaxAmount,
    decimal FurtherTaxAmount,
    decimal TaxAmount,
    decimal Discount,
    decimal LineTotal,
    string SaleType);

public record FbrSubmissionResult(
    bool Success,
    string? Message,
    string? FbrInvoiceNumber,
    string? ResponseJson,
    bool IsSimulation);

public record FbrPayloadPreviewDto(
    int SalesInvoiceId,
    string InvoiceNumber,
    string PayloadJson,
    bool IsSimulationMode,
    string FooterNotice);
