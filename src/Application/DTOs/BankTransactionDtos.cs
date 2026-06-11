using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;
public record BankTransactionDto(
    int Id,
    int ChartOfAccountId,
    string AccountLabel,
    BankTransactionType TransactionType,
    int? TransferToChartOfAccountId,
    string? TransferToAccountLabel,
    int? CounterChartOfAccountId,
    string? PartyName,
    PaymentMethod? PaymentMethod,
    DateTime TransactionDate,
    string? ChequeNumber,
    DateTime? ChequeDate,
    decimal Amount,
    string? Description,
    bool IsReconciled,
    int? JournalEntryId);

public record BankTransactionListItemDto(
    int Id,
    string AccountLabel,
    DateTime TransactionDate,
    string TransactionType,
    string? TransferToAccountLabel,
    decimal Amount,
    string? Description,
    string? PartyName,
    string? PaymentMethod,
    string? ChequeNumber,
    bool IsReconciled);

public class BankTransactionSaveRequest
{
    public int ChartOfAccountId { get; set; }
    public BankTransactionType TransactionType { get; set; }
    public int? TransferToChartOfAccountId { get; set; }
    public int? CounterChartOfAccountId { get; set; }
    public string? PartyName { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? ChequeNumber { get; set; }
    public DateTime? ChequeDate { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public List<int> CustomerReceiptIds { get; set; } = [];
    public int? CustomerId { get; set; }
    public int? VendorId { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
}

public record UndepositedChequeDto(
    int Id,
    string CustomerName,
    string ReceiptNumber,
    string? ChequeNumber,
    decimal Amount,
    DateTime ReceiptDate,
    DateTime? ChequeDate,
    bool IsPostDated);

public record BankTransactionSaveResult(bool Success, string? Message, BankTransactionDto? Transaction);

public record BankCoaLookupDto(
    int Id,
    string AccountNumber,
    string AccountName,
    decimal Balance)
{
    public string Label => AccountNumber + " — " + AccountName;
}

public record WriteChequePartyLookupDto(
    int ChartOfAccountId,
    string PartyType,
    int? CustomerId,
    int? VendorId,
    string PartyName,
    string AccountNumber,
    decimal Balance)
{
    public string Label => $"[{PartyType}] {PartyName} — {AccountNumber}";
}

public record BankUndepositedSummaryDto(decimal Balance, string AccountNumber);

public record BankNextChequeNumberDto(string? NextChequeNumber, bool IsConfigured);

public class BankNextChequeNumberSaveRequest
{
    public int ChartOfAccountId { get; set; }
    public string NextChequeNumber { get; set; } = string.Empty;
}

public record BankNextChequeNumberSaveResult(bool Success, string? Message, BankNextChequeNumberDto? NextChequeNumber);
