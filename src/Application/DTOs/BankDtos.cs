namespace PakistanAccountingERP.Application.DTOs;

public record BankDto(
    int Id,
    string BankName,
    string AccountTitle,
    string AccountNumber,
    string? IBAN,
    int? ChartOfAccountId,
    string? ChartOfAccountLabel,
    decimal OpeningBalance,
    decimal CurrentBalance,
    bool IsActive,
    int TransactionCount,
    bool IsUsedOnPayments);

public record BankListItemDto(
    int Id,
    string BankName,
    string AccountTitle,
    string AccountNumber,
    decimal CurrentBalance,
    bool IsActive,
    int TransactionCount);

public class BankSaveRequest
{
    public int? Id { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string AccountTitle { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string? IBAN { get; set; }
    public int? ChartOfAccountId { get; set; }
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;
}

public record BankSaveResult(bool Success, string? Message, BankDto? Bank);

public record BankChartOfAccountLookupDto(int Id, string AccountNumber, string AccountName);
