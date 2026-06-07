using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record DatabaseBackupHistoryListItemDto(
    int Id,
    string FileName,
    long FileSizeBytes,
    JobRunType RunType,
    JobRunStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ErrorMessage,
    string? CreatedBy);

public record DataExportHistoryListItemDto(
    int Id,
    DataExportType ExportType,
    string FileName,
    long FileSizeBytes,
    JobRunStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ErrorMessage,
    string? CreatedBy);

public record JobActionResult(bool Success, string? Message, int? Id = null);
