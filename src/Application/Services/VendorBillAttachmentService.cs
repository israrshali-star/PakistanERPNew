using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Application.Options;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class VendorBillAttachmentService : IVendorBillAttachmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly AttachmentOptions _options;
    private readonly ILogger<VendorBillAttachmentService> _logger;

    public VendorBillAttachmentService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IOptions<AttachmentOptions> options,
        ILogger<VendorBillAttachmentService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DocumentAttachmentDto>> GetByBillIdAsync(
        int billId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<VendorBillAttachment>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.VendorBillId == billId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new DocumentAttachmentDto(
                a.Id,
                a.FileName,
                a.ContentType,
                a.FileSizeBytes,
                a.CreatedAt,
                a.CreatedBy))
            .ToListAsync(cancellationToken);
    }

    public async Task<DocumentAttachmentSaveResult> UploadAsync(
        int billId,
        string fileName,
        string contentType,
        Stream content,
        long fileSizeBytes,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var validation = AttachmentFileRules.Validate(fileName, contentType, fileSizeBytes, _options);
        if (!validation.Success)
        {
            return validation;
        }

        var bill = await _unitOfWork.Repository<VendorBill>()
            .Query()
            .Where(b => b.Id == billId && b.CompanyId == companyId)
            .Select(b => new { b.Id, b.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (bill is null)
        {
            return new DocumentAttachmentSaveResult(false, "Bill not found.", null);
        }

        if (bill.Status != BillStatus.Draft)
        {
            return new DocumentAttachmentSaveResult(false, "Attachments can only be added to draft bills.", null);
        }

        var maxFiles = _options.MaxFilesPerBill > 0 ? _options.MaxFilesPerBill : _options.MaxFilesPerInvoice;
        var existingCount = await _unitOfWork.Repository<VendorBillAttachment>()
            .Query()
            .CountAsync(a => a.VendorBillId == billId && a.CompanyId == companyId, cancellationToken);

        if (existingCount >= maxFiles)
        {
            return new DocumentAttachmentSaveResult(
                false,
                $"Maximum {maxFiles} attachments allowed per bill.",
                null);
        }

        var extension = Path.GetExtension(fileName);
        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var relativeDirectory = Path.Combine("vendor-bills", companyId.ToString(), billId.ToString());
        var absoluteDirectory = Path.Combine(AttachmentFileRules.GetStorageRoot(_options), relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        var absolutePath = Path.Combine(absoluteDirectory, storedFileName);
        var relativePath = Path.Combine(relativeDirectory, storedFileName).Replace('\\', '/');
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "system";

        try
        {
            await using (var fileStream = new FileStream(absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(fileStream, cancellationToken);
            }

            var entity = new VendorBillAttachment
            {
                CompanyId = companyId,
                VendorBillId = billId,
                FileName = Path.GetFileName(fileName),
                StoredFileName = storedFileName,
                ContentType = contentType,
                FileSizeBytes = fileSizeBytes,
                RelativePath = relativePath,
                CreatedAt = now,
                CreatedBy = userName
            };

            await _unitOfWork.Repository<VendorBillAttachment>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new DocumentAttachmentSaveResult(
                true,
                null,
                new DocumentAttachmentDto(
                    entity.Id,
                    entity.FileName,
                    entity.ContentType,
                    entity.FileSizeBytes,
                    entity.CreatedAt,
                    entity.CreatedBy));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save attachment for vendor bill {BillId}", billId);
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            return new DocumentAttachmentSaveResult(false, "Could not save attachment.", null);
        }
    }

    public async Task<DocumentAttachmentDownloadDto?> DownloadAsync(
        int attachmentId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var attachment = await _unitOfWork.Repository<VendorBillAttachment>()
            .Query()
            .Where(a => a.Id == attachmentId && a.CompanyId == companyId)
            .FirstOrDefaultAsync(cancellationToken);

        if (attachment is null)
        {
            return null;
        }

        var absolutePath = Path.Combine(
            AttachmentFileRules.GetStorageRoot(_options),
            attachment.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(absolutePath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(absolutePath, cancellationToken);
        return new DocumentAttachmentDownloadDto(attachment.FileName, attachment.ContentType, bytes);
    }

    public async Task<DocumentAttachmentSaveResult> DeleteAsync(
        int attachmentId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var attachment = await _unitOfWork.Repository<VendorBillAttachment>()
            .Query(asNoTracking: false)
            .Include(a => a.VendorBill)
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.CompanyId == companyId, cancellationToken);

        if (attachment is null)
        {
            return new DocumentAttachmentSaveResult(false, "Attachment not found.", null);
        }

        if (attachment.VendorBill.Status != BillStatus.Draft)
        {
            return new DocumentAttachmentSaveResult(false, "Attachments can only be removed from draft bills.", null);
        }

        var absolutePath = Path.Combine(
            AttachmentFileRules.GetStorageRoot(_options),
            attachment.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        _unitOfWork.Repository<VendorBillAttachment>().Remove(attachment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return new DocumentAttachmentSaveResult(true, "Attachment deleted.", null);
    }
}
