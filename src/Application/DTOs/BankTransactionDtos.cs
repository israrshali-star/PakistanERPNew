using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record BankTransactionDto(
    int Id,
    int BankId,
    string BankName,
    BankTransactionType TransactionType,
    int? TransferToBankId,
    string? TransferToBankName,
    DateTime TransactionDate,
    string? ChequeNumber,
    DateTime? ChequeDate,
    decimal Amount,
    string? Description,
    bool IsReconciled);

public record BankTransactionListItemDto(
    int Id,
    string BankName,
    DateTime TransactionDate,
    string TransactionType,
    string? TransferToBankName,
    decimal Amount,
    string? Description,
    bool IsReconciled);

public class BankTransactionSaveRequest
{
    public int BankId { get; set; }
    public BankTransactionType TransactionType { get; set; }
    public int? TransferToBankId { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? ChequeNumber { get; set; }
    public DateTime? ChequeDate { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
}

public record BankTransactionSaveResult(bool Success, string? Message, BankTransactionDto? Transaction);

public record BankTransactionBankLookupDto(
    int Id,
    string BankName,
    string AccountNumber,
    decimal CurrentBalance);
