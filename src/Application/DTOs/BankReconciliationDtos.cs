namespace PakistanAccountingERP.Application.DTOs;

public record BankReconciliationUnreconciledDto(
    int Id,
    DateTime TransactionDate,
    string TransactionType,
    decimal Amount,
    string? Description,
    string? ChequeNumber);

public record BankReconciliationPreviewDto(
    int BankId,
    string BankName,
    string AccountNumber,
    decimal BookBalance,
    int UnreconciledCount,
    IReadOnlyList<BankReconciliationUnreconciledDto> UnreconciledTransactions);

public record BankReconciliationListItemDto(
    int Id,
    string BankName,
    DateTime StatementDate,
    decimal StatementBalance,
    decimal BookBalance,
    decimal Difference,
    bool IsCompleted,
    DateTime CreatedAt);

public class BankReconciliationCompleteRequest
{
    public int BankId { get; set; }
    public DateTime StatementDate { get; set; }
    public decimal StatementBalance { get; set; }
    public List<int> TransactionIds { get; set; } = new();
}

public record BankReconciliationCompleteResult(bool Success, string? Message, int? ReconciliationId);

public record BankReconciliationBankLookupDto(int Id, string BankName, string AccountNumber);
