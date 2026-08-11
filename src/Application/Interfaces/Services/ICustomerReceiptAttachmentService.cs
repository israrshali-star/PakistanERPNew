using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ICustomerReceiptAttachmentService
{
    Task<IReadOnlyList<DocumentAttachmentDto>> GetByReceiptIdAsync(
        int receiptId,
        CancellationToken cancellationToken = default);

    Task<DocumentAttachmentSaveResult> UploadAsync(
        int receiptId,
        string fileName,
        string contentType,
        Stream content,
        long fileSizeBytes,
        CancellationToken cancellationToken = default);

    Task<DocumentAttachmentDownloadDto?> DownloadAsync(
        int attachmentId,
        CancellationToken cancellationToken = default);

    Task<DocumentAttachmentSaveResult> DeleteAsync(
        int attachmentId,
        CancellationToken cancellationToken = default);
}
