using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Options;

namespace PakistanAccountingERP.Application.Common;

public static class AttachmentFileRules
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

    public static DocumentAttachmentSaveResult Validate(string fileName, string contentType, long fileSizeBytes, AttachmentOptions options)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new DocumentAttachmentSaveResult(false, "File name is required.", null);
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            return new DocumentAttachmentSaveResult(false, "Only JPG, PNG, and PDF files are allowed.", null);
        }

        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
        {
            return new DocumentAttachmentSaveResult(false, "Invalid file type. Only JPG, PNG, and PDF are allowed.", null);
        }

        var maxBytes = options.MaxFileSizeMb * 1024L * 1024L;
        if (fileSizeBytes <= 0 || fileSizeBytes > maxBytes)
        {
            return new DocumentAttachmentSaveResult(
                false,
                $"File size must be between 1 byte and {options.MaxFileSizeMb} MB.",
                null);
        }

        return new DocumentAttachmentSaveResult(true, null, null);
    }

    public static string GetStorageRoot(AttachmentOptions options)
    {
        var raw = string.IsNullOrWhiteSpace(options.StoragePath) ? "App_Data/Attachments" : options.StoragePath;
        return Path.IsPathRooted(raw) ? raw : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, raw));
    }
}
