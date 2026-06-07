using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IVendorBillAttachmentService
{
    Task<IReadOnlyList<DocumentAttachmentDto>> GetByBillIdAsync(
        int billId,
        CancellationToken cancellationToken = default);

    Task<DocumentAttachmentSaveResult> UploadAsync(
        int billId,
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
