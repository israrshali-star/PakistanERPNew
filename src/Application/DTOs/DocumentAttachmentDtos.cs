namespace PakistanAccountingERP.Application.DTOs;

public record DocumentAttachmentDto(
    int Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTime CreatedAt,
    string? CreatedBy);

public record DocumentAttachmentSaveResult(
    bool Success,
    string? Message,
    DocumentAttachmentDto? Attachment);

public record DocumentAttachmentDownloadDto(
    string FileName,
    string ContentType,
    byte[] Content);
