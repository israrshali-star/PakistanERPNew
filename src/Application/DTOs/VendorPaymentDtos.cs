using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record VendorPaymentDto(
    int Id,
    string PaymentNumber,
    int VendorId,
    string VendorName,
    string VendorCode,
    DateTime PaymentDate,
    decimal Amount,
    PaymentMethod PaymentMethod,
    int? BankId,
    string? BankName,
    string? ChequeNumber,
    DateTime? ChequeDate,
    string? Notes);

public record VendorPaymentListItemDto(
    int Id,
    string PaymentNumber,
    string VendorName,
    DateTime PaymentDate,
    decimal Amount,
    string PaymentMethod,
    string? BankName);

public class VendorPaymentSaveRequest
{
    public int? Id { get; set; }
    public string PaymentNumber { get; set; } = string.Empty;
    public int VendorId { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public int? BankId { get; set; }
    public string? ChequeNumber { get; set; }
    public DateTime? ChequeDate { get; set; }
    public string? Notes { get; set; }
}

public record VendorPaymentSaveResult(bool Success, string? Message, VendorPaymentDto? Payment);

public record NextVendorPaymentNumberDto(string PaymentNumber);

public record VendorPaymentVendorLookupDto(
    int Id,
    string VendorCode,
    string VendorName,
    decimal Balance);

public record VendorPaymentBankLookupDto(int Id, string BankName, string AccountNumber);
