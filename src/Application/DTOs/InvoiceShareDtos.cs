namespace PakistanAccountingERP.Application.DTOs;

public record SalesInvoiceShareInfoDto(
    int InvoiceId,
    string InvoiceNumber,
    string? FbrInvoiceNumber,
    string CustomerName,
    string? CustomerEmail,
    string? CustomerMobile,
    string? CustomerPhone,
    string? SellerCompanyName,
    DateTime InvoiceDate,
    decimal NetTotal,
    bool CanShare,
    string WhatsAppMessage,
    bool EmailConfigured,
    string? GodownEmail,
    bool CanEmailChallan);

public record SalesInvoiceEmailShareRequest(string ToEmail, string? Message);

public record SalesInvoiceChallanEmailShareRequest(string? ToEmail, string? Message);

public record SalesInvoiceShareActionResult(bool Success, string? Message);
