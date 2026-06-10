using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record JournalEntryListItemDto(
    int Id,
    string EntryNumber,
    DateTime EntryDate,
    string? Description,
    string SourceLabel,
    decimal TotalDebit,
    string Status,
    bool CanPost,
    bool CanDelete,
    bool CanEdit);

public record JournalEntryLineDto(
    int Id,
    int ChartOfAccountId,
    string AccountNumber,
    string AccountName,
    decimal Debit,
    decimal Credit,
    string? Memo);

public record JournalEntryDetailDto(
    int Id,
    string EntryNumber,
    DateTime EntryDate,
    string? Description,
    string? ReferenceType,
    int? ReferenceId,
    string SourceLabel,
    string? SourceUrl,
    JournalStatus Status,
    decimal TotalDebit,
    decimal TotalCredit,
    bool CanPost,
    bool CanDelete,
    bool CanEdit,
    IReadOnlyList<JournalEntryLineDto> Lines);

public class JournalEntryLineSaveRequest
{
    public int ChartOfAccountId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Memo { get; set; }
}

public class JournalEntrySaveRequest
{
    public int? Id { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; }
    public string? Description { get; set; }
    public List<JournalEntryLineSaveRequest> Lines { get; set; } = new();
}

public record JournalEntrySaveResult(bool Success, string? Message, int? EntryId);

public record JournalEntryActionResult(bool Success, string? Message, JournalEntryDetailDto? Entry);

public record NextJournalEntryNumberDto(string EntryNumber);

public record JournalEntryAccountLookupDto(int Id, string AccountNumber, string AccountName);
