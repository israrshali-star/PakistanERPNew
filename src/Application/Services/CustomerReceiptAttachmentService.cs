using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Application.Options;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Services;

public class CustomerReceiptAttachmentService : ICustomerReceiptAttachmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly AttachmentOptions _options;
    private readonly ILogger<CustomerReceiptAttachmentService> _logger;

    public CustomerReceiptAttachmentService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IOptions<AttachmentOptions> options,
        ILogger<CustomerReceiptAttachmentService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DocumentAttachmentDto>> GetByReceiptIdAsync(
        int receiptId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<CustomerReceiptAttachment>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.CustomerReceiptId == receiptId)
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
        int receiptId,
        string fileName,
        string contentType,
        Stream content,
        long fileSizeBytes,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var normalizedType = AttachmentFileRules.NormalizeContentType(fileName, contentType);
        var validation = AttachmentFileRules.Validate(fileName, normalizedType, fileSizeBytes, _options);
        if (!validation.Success)
        {
            return validation;
        }

        var receiptExists = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .AnyAsync(r => r.Id == receiptId && r.CompanyId == companyId, cancellationToken);

        if (!receiptExists)
        {
            return new DocumentAttachmentSaveResult(false, "Receipt not found.", null);
        }

        var maxFiles = TradeInvoiceLayout.GetCustomerReceiptAttachmentLimit(companyId)
            ?? (_options.MaxFilesPerReceipt > 0
                ? _options.MaxFilesPerReceipt
                : (_options.MaxFilesPerInvoice > 0 ? _options.MaxFilesPerInvoice : 10));

        var existingCount = await _unitOfWork.Repository<CustomerReceiptAttachment>()
            .Query()
            .CountAsync(a => a.CustomerReceiptId == receiptId && a.CompanyId == companyId, cancellationToken);

        if (existingCount >= maxFiles)
        {
            return new DocumentAttachmentSaveResult(
                false,
                $"Maximum {maxFiles} attachments allowed per receipt.",
                null);
        }

        var extension = Path.GetExtension(fileName);
        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var relativeDirectory = Path.Combine("customer-receipts", companyId.ToString(), receiptId.ToString());
        var relativePath = Path.Combine(relativeDirectory, storedFileName).Replace('\\', '/');
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "system";
        var absolutePath = string.Empty;

        try
        {
            var absoluteDirectory = Path.Combine(AttachmentFileRules.GetStorageRoot(_options), relativeDirectory);
            Directory.CreateDirectory(absoluteDirectory);
            absolutePath = Path.Combine(absoluteDirectory, storedFileName);

            await using (var fileStream = new FileStream(absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(fileStream, cancellationToken);
            }

            var entity = new CustomerReceiptAttachment
            {
                CompanyId = companyId,
                CustomerReceiptId = receiptId,
                FileName = Path.GetFileName(fileName),
                StoredFileName = storedFileName,
                ContentType = normalizedType,
                FileSizeBytes = fileSizeBytes,
                RelativePath = relativePath,
                CreatedAt = now,
                CreatedBy = userName,
                UpdatedAt = now,
                UpdatedBy = userName
            };

            await _unitOfWork.Repository<CustomerReceiptAttachment>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new DocumentAttachmentSaveResult(
                true,
                "Attachment uploaded.",
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
            _logger.LogError(ex, "Failed to upload attachment for receipt {ReceiptId} to {Path}", receiptId, absolutePath);
            TryDeleteFile(absolutePath);
            return new DocumentAttachmentSaveResult(false, AttachmentFileRules.DescribeSaveFailure(ex), null);
        }
    }

    public async Task<DocumentAttachmentDownloadDto?> DownloadAsync(
        int attachmentId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var attachment = await _unitOfWork.Repository<CustomerReceiptAttachment>()
            .Query()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.CompanyId == companyId, cancellationToken);

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
        var attachment = await _unitOfWork.Repository<CustomerReceiptAttachment>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.CompanyId == companyId, cancellationToken);

        if (attachment is null)
        {
            return new DocumentAttachmentSaveResult(false, "Attachment not found.", null);
        }

        var absolutePath = Path.Combine(
            AttachmentFileRules.GetStorageRoot(_options),
            attachment.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        _unitOfWork.Repository<CustomerReceiptAttachment>().Remove(attachment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        TryDeleteFile(absolutePath);

        return new DocumentAttachmentSaveResult(true, "Attachment deleted.", null);
    }

    private static void TryDeleteFile(string absolutePath)
    {
        try
        {
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
