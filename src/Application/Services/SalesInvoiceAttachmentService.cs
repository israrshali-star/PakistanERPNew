using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Application.Options;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class SalesInvoiceAttachmentService : ISalesInvoiceAttachmentService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".pdf"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "application/pdf"
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly AttachmentOptions _options;
    private readonly ILogger<SalesInvoiceAttachmentService> _logger;

    public SalesInvoiceAttachmentService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IOptions<AttachmentOptions> options,
        ILogger<SalesInvoiceAttachmentService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SalesInvoiceAttachmentDto>> GetByInvoiceIdAsync(
        int invoiceId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<SalesInvoiceAttachment>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.SalesInvoiceId == invoiceId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new SalesInvoiceAttachmentDto(
                a.Id,
                a.FileName,
                a.ContentType,
                a.FileSizeBytes,
                a.CreatedAt,
                a.CreatedBy))
            .ToListAsync(cancellationToken);
    }

    public async Task<SalesInvoiceAttachmentSaveResult> UploadAsync(
        int invoiceId,
        string fileName,
        string contentType,
        Stream content,
        long fileSizeBytes,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var validation = ValidateFile(fileName, contentType, fileSizeBytes);
        if (!validation.Success)
        {
            return validation;
        }

        var invoice = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.Id == invoiceId && i.CompanyId == companyId)
            .Select(i => new { i.Id, i.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (invoice is null)
        {
            return new SalesInvoiceAttachmentSaveResult(false, "Invoice not found.", null);
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return new SalesInvoiceAttachmentSaveResult(false, "Attachments can only be added to draft invoices.", null);
        }

        var existingCount = await _unitOfWork.Repository<SalesInvoiceAttachment>()
            .Query()
            .CountAsync(a => a.SalesInvoiceId == invoiceId && a.CompanyId == companyId, cancellationToken);

        if (existingCount >= _options.MaxFilesPerInvoice)
        {
            return new SalesInvoiceAttachmentSaveResult(
                false,
                $"Maximum {_options.MaxFilesPerInvoice} attachments allowed per invoice.",
                null);
        }

        var extension = Path.GetExtension(fileName);
        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var relativeDirectory = Path.Combine(companyId.ToString(), invoiceId.ToString());
        var absoluteDirectory = Path.Combine(GetStorageRoot(), relativeDirectory);
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

            var entity = new SalesInvoiceAttachment
            {
                CompanyId = companyId,
                SalesInvoiceId = invoiceId,
                FileName = Path.GetFileName(fileName),
                StoredFileName = storedFileName,
                ContentType = contentType,
                FileSizeBytes = fileSizeBytes,
                RelativePath = relativePath,
                CreatedAt = now,
                CreatedBy = userName
            };

            await _unitOfWork.Repository<SalesInvoiceAttachment>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new SalesInvoiceAttachmentSaveResult(
                true,
                null,
                new SalesInvoiceAttachmentDto(
                    entity.Id,
                    entity.FileName,
                    entity.ContentType,
                    entity.FileSizeBytes,
                    entity.CreatedAt,
                    entity.CreatedBy));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save attachment for invoice {InvoiceId}", invoiceId);
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            return new SalesInvoiceAttachmentSaveResult(false, "Could not save attachment.", null);
        }
    }

    public async Task<SalesInvoiceAttachmentDownloadDto?> DownloadAsync(
        int attachmentId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var attachment = await _unitOfWork.Repository<SalesInvoiceAttachment>()
            .Query()
            .Where(a => a.Id == attachmentId && a.CompanyId == companyId)
            .FirstOrDefaultAsync(cancellationToken);

        if (attachment is null)
        {
            return null;
        }

        var absolutePath = Path.Combine(GetStorageRoot(), attachment.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolutePath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(absolutePath, cancellationToken);
        return new SalesInvoiceAttachmentDownloadDto(attachment.FileName, attachment.ContentType, bytes);
    }

    public async Task<SalesInvoiceAttachmentSaveResult> DeleteAsync(
        int attachmentId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var attachment = await _unitOfWork.Repository<SalesInvoiceAttachment>()
            .Query(asNoTracking: false)
            .Include(a => a.SalesInvoice)
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.CompanyId == companyId, cancellationToken);

        if (attachment is null)
        {
            return new SalesInvoiceAttachmentSaveResult(false, "Attachment not found.", null);
        }

        if (attachment.SalesInvoice.Status != InvoiceStatus.Draft)
        {
            return new SalesInvoiceAttachmentSaveResult(false, "Attachments can only be removed from draft invoices.", null);
        }

        var absolutePath = Path.Combine(GetStorageRoot(), attachment.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        _unitOfWork.Repository<SalesInvoiceAttachment>().Remove(attachment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return new SalesInvoiceAttachmentSaveResult(true, "Attachment deleted.", null);
    }

    private SalesInvoiceAttachmentSaveResult ValidateFile(string fileName, string contentType, long fileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new SalesInvoiceAttachmentSaveResult(false, "File name is required.", null);
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            return new SalesInvoiceAttachmentSaveResult(false, "Only JPG, PNG, and PDF files are allowed.", null);
        }

        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
        {
            return new SalesInvoiceAttachmentSaveResult(false, "Invalid file type. Only JPG, PNG, and PDF are allowed.", null);
        }

        var maxBytes = _options.MaxFileSizeMb * 1024L * 1024L;
        if (fileSizeBytes <= 0 || fileSizeBytes > maxBytes)
        {
            return new SalesInvoiceAttachmentSaveResult(
                false,
                $"File size must be between 1 byte and {_options.MaxFileSizeMb} MB.",
                null);
        }

        return new SalesInvoiceAttachmentSaveResult(true, null, null);
    }

    private string GetStorageRoot()
    {
        var raw = string.IsNullOrWhiteSpace(_options.StoragePath) ? "App_Data/Attachments" : _options.StoragePath;
        return Path.IsPathRooted(raw) ? raw : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, raw));
    }
}
