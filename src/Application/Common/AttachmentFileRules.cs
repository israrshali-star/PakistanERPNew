using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Options;

namespace PakistanAccountingERP.Application.Common;

public static class AttachmentFileRules
{
    public static readonly string DefaultStoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PakistanAccountingERP",
        "Attachments");

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".pdf"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/pjpeg",
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
        if (string.Equals(extension, ".heic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".heif", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
        {
            return new DocumentAttachmentSaveResult(
                false,
                "Only JPG, PNG, and PDF files are allowed. Convert iPhone HEIC photos to JPG before uploading.",
                null);
        }

        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            return new DocumentAttachmentSaveResult(false, "Only JPG, PNG, and PDF files are allowed.", null);
        }

        var normalizedType = NormalizeContentType(fileName, contentType);
        if (string.IsNullOrWhiteSpace(normalizedType) || !AllowedContentTypes.Contains(normalizedType))
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

    /// <summary>
    /// Browsers (especially after IIS publish / phone uploads) often send empty,
    /// <c>application/octet-stream</c>, or <c>image/jpg</c> instead of a standard MIME type.
    /// </summary>
    public static string NormalizeContentType(string fileName, string? contentType)
    {
        var inferred = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".pdf" => "application/pdf",
            _ => string.Empty
        };

        var raw = (contentType ?? string.Empty).Split(';')[0].Trim();
        if (string.Equals(raw, "image/jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "image/pjpeg", StringComparison.OrdinalIgnoreCase))
        {
            raw = "image/jpeg";
        }

        if (string.IsNullOrWhiteSpace(raw)
            || string.Equals(raw, "application/octet-stream", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "binary/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return inferred;
        }

        return raw;
    }

    public static string DescribeSaveFailure(Exception ex)
    {
        if (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return "Could not save the file on the server. After publishing, grant the IIS app pool Modify permission on "
                + DefaultStoragePath + ".";
        }

        return "Could not save attachment.";
    }

    public static string GetStorageRoot(AttachmentOptions options)
    {
        var raw = string.IsNullOrWhiteSpace(options.StoragePath)
            ? DefaultStoragePath
            : options.StoragePath.Trim();

        var path = Path.IsPathRooted(raw)
            ? raw
            : Path.GetFullPath(Path.Combine(DefaultStoragePath, raw));

        // Relative App_Data paths sit inside bin/ or the IIS site folder. Those are not
        // writable for ApplicationPoolIdentity and are wiped on every publish.
        if (IsVolatileAppPath(path))
        {
            path = DefaultStoragePath;
        }

        Directory.CreateDirectory(path);
        return path;
    }

    private static bool IsVolatileAppPath(string path)
    {
        return path.Contains("App_Data", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}publish{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }
}
