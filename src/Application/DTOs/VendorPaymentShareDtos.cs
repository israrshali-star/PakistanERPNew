namespace PakistanAccountingERP.Application.DTOs;

public record VendorPaymentPdfDto(
    string CompanyName,
    string PaymentNumber,
    string VendorName,
    string VendorCode,
    DateTime PaymentDate,
    decimal Amount,
    string PaymentMethodLabel,
    string? BankName,
    string? ChequeNumber,
    DateTime? ChequeDate,
    string? Notes);

public record VendorPaymentShareInfoDto(
    int PaymentId,
    string PaymentNumber,
    string VendorName,
    string VendorCode,
    DateTime PaymentDate,
    decimal Amount,
    string PaymentMethodLabel,
    string? VendorEmail,
    string? VendorPhone,
    string CompanyName,
    string WhatsAppMessage);
