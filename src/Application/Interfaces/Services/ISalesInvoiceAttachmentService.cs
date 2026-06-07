using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ISalesInvoiceAttachmentService
{
    Task<IReadOnlyList<SalesInvoiceAttachmentDto>> GetByInvoiceIdAsync(
        int invoiceId,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceAttachmentSaveResult> UploadAsync(
        int invoiceId,
        string fileName,
        string contentType,
        Stream content,
        long fileSizeBytes,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceAttachmentDownloadDto?> DownloadAsync(
        int attachmentId,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceAttachmentSaveResult> DeleteAsync(
        int attachmentId,
        CancellationToken cancellationToken = default);
}
