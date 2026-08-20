namespace PakistanAccountingERP.Application.DTOs;

public record CustomerReceiptInvoiceLineDto(
    string InvoiceNumber,
    DateTime? InvoiceDate,
    decimal AppliedAmount);

public record CustomerReceiptInvoiceAllocationDto(
    decimal OutstandingBefore,
    decimal RemainingBalance,
    decimal UnallocatedAmount,
    IReadOnlyList<CustomerReceiptInvoiceLineDto> Invoices);

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
    bool UseUrdu = false,
    decimal? RemainingBalance = null,
    IReadOnlyList<CustomerReceiptInvoiceLineDto>? InvoicesPaid = null);

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
