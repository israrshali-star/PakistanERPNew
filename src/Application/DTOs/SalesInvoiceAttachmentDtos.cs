namespace PakistanAccountingERP.Application.DTOs;

public record SalesInvoiceAttachmentDto(
    int Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTime CreatedAt,
    string? CreatedBy);

public record SalesInvoiceAttachmentSaveResult(
    bool Success,
    string? Message,
    SalesInvoiceAttachmentDto? Attachment);

public record SalesInvoiceAttachmentDownloadDto(
    string FileName,
    string ContentType,
    byte[] Content);
