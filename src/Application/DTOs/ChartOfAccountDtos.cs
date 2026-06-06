namespace PakistanAccountingERP.Application.DTOs;

public record ChartOfAccountDto(
    int Id,
    string AccountNumber,
    string AccountName,
    int? TypeId,
    string? TypeName,
    int? SubTypeId,
    string? SubTypeName,
    int? ParentAccountId,
    string? ParentAccountName,
    string? Description,
    decimal OpeningBalance,
    decimal RunningBalance,
    bool IsActive,
    bool IsGroupAccount,
    bool HasChildren,
    bool HasJournalLines,
    bool IsLinkedToBank);

public record ChartOfAccountTreeAccountDto(
    int Id,
    string AccountNumber,
    string AccountName,
    decimal OpeningBalance,
    decimal RunningBalance,
    bool IsActive,
    bool IsGroupAccount,
    int? ParentAccountId,
    IReadOnlyList<ChartOfAccountTreeAccountDto> Children);

public record ChartOfAccountTreeSubTypeDto(
    int SubTypeId,
    string SubTypeCode,
    string SubTypeName,
    IReadOnlyList<ChartOfAccountTreeAccountDto> Accounts);

public record ChartOfAccountTreeTypeDto(
    int TypeId,
    string TypeCode,
    string TypeName,
    IReadOnlyList<ChartOfAccountTreeSubTypeDto> SubTypes);

public class ChartOfAccountSaveRequest
{
    public int? Id { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public int TypeId { get; set; }
    public int SubTypeId { get; set; }
    public int? ParentAccountId { get; set; }
    public string? Description { get; set; }
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;
}

public record ChartOfAccountSaveResult(
    bool Success,
    string? Message,
    ChartOfAccountDto? Account,
    int? ExistingAccountId = null);

public record SuggestedAccountNumberDto(string AccountNumber);

public record ParentAccountLookupDto(int Id, string AccountNumber, string AccountName);
