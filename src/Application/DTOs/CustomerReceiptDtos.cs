using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record CustomerReceiptDto(
    int Id,
    string ReceiptNumber,
    int CustomerId,
    string CustomerName,
    string CustomerCode,
    DateTime ReceiptDate,
    decimal Amount,
    PaymentMethod PaymentMethod,
    int? BankId,
    string? BankName,
    string? ChequeNumber,
    DateTime? ChequeDate,
    string? Notes);

public record CustomerReceiptListItemDto(
    int Id,
    string ReceiptNumber,
    string CustomerName,
    DateTime ReceiptDate,
    decimal Amount,
    string PaymentMethod,
    string? BankName);

public class CustomerReceiptSaveRequest
{
    public int? Id { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public DateTime ReceiptDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public int? BankId { get; set; }
    public string? ChequeNumber { get; set; }
    public DateTime? ChequeDate { get; set; }
    public string? Notes { get; set; }
}

public record CustomerReceiptSaveResult(bool Success, string? Message, CustomerReceiptDto? Receipt);

public record NextReceiptNumberDto(string ReceiptNumber);

public record CustomerReceiptCustomerLookupDto(
    int Id,
    string BuyerId,
    string BuyerName,
    decimal Balance);

public record CustomerReceiptBankLookupDto(int Id, string BankName, string AccountNumber);
