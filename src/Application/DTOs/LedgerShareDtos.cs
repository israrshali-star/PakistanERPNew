namespace PakistanAccountingERP.Application.DTOs;

public record PartyLedgerPdfLineDto(
    DateTime Date,
    string Reference,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal Balance,
    decimal PendingCredit = 0m);

public record PartyLedgerPdfDto(
    string Title,
    string PartyName,
    string PartyCode,
    string? PartyNtn,
    string CompanyName,
    string? PeriodLabel,
    decimal OpeningBalance,
    decimal ClosingBalance,
    bool ShowPendingColumn,
    IReadOnlyList<PartyLedgerPdfLineDto> Lines);

public record LedgerShareInfoDto(
    string PartyType,
    int PartyId,
    string PartyName,
    string PartyCode,
    string? PartyEmail,
    string? PartyMobile,
    string? PartyPhone,
    string CompanyName,
    string? PeriodLabel,
    decimal ClosingBalance,
    string WhatsAppMessage,
    bool EmailConfigured,
    DateTime? FromDate,
    DateTime? ToDate);

public record LedgerEmailShareRequest(
    string ToEmail,
    string? Message,
    DateTime? FromDate,
    DateTime? ToDate);

public record LedgerShareActionResult(bool Success, string? Message);
