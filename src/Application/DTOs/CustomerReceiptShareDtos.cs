namespace PakistanAccountingERP.Application.DTOs;

public record CustomerReceiptPdfDto(
    string CompanyName,
    string ReceiptNumber,
    string CustomerName,
    string CustomerCode,
    DateTime ReceiptDate,
    decimal Amount,
    decimal TotalAmountDue,
    string PaymentMethodLabel,
    string? BankName,
    string? ChequeNumber,
    DateTime? ChequeDate,
    string? Notes,
    string StatusLabel,
    bool UseUrdu = false);

public record CustomerReceiptShareInfoDto(
    int ReceiptId,
    string ReceiptNumber,
    string CustomerName,
    string CustomerCode,
    DateTime ReceiptDate,
    decimal Amount,
    string PaymentMethodLabel,
    string? CustomerEmail,
    string? CustomerMobile,
    string? CustomerPhone,
    string CompanyName,
    string WhatsAppMessage,
    bool SupportsUrduReceipt = false,
    string? WhatsAppMessageUrdu = null,
    string? CustomerNameUrdu = null);
