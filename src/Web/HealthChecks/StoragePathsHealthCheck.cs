using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PakistanAccountingERP.Application.Options;
using PakistanAccountingERP.Infrastructure.Options;

namespace PakistanAccountingERP.Web.HealthChecks;

public class StoragePathsHealthCheck : IHealthCheck
{
    private readonly BackupOptions _backupOptions;
    private readonly ExportOptions _exportOptions;
    private readonly AttachmentOptions _attachmentOptions;

    public StoragePathsHealthCheck(
        IOptions<BackupOptions> backupOptions,
        IOptions<ExportOptions> exportOptions,
        IOptions<AttachmentOptions> attachmentOptions)
    {
        _backupOptions = backupOptions.Value;
        _exportOptions = exportOptions.Value;
        _attachmentOptions = attachmentOptions.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();

        CheckWritablePath("backup", _backupOptions.StoragePath, issues);
        CheckWritablePath("export", _exportOptions.StoragePath, issues);
        CheckWritablePath("attachments", _attachmentOptions.StoragePath, issues);

        if (issues.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Backup, export, and attachment storage paths are writable."));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy(string.Join(" ", issues)));
    }

    private static void CheckWritablePath(string label, string? configuredPath, ICollection<string> issues)
    {
        var path = ResolveStoragePath(configuredPath);

        try
        {
            Directory.CreateDirectory(path);

            var probeFile = Path.Combine(path, $".health-{Guid.NewGuid():N}");
            File.WriteAllText(probeFile, "ok");
            File.Delete(probeFile);
        }
        catch (Exception ex)
        {
            issues.Add($"{label} storage unavailable at '{path}': {ex.Message}");
        }
    }

    private static string ResolveStoragePath(string? configuredPath)
    {
        var raw = string.IsNullOrWhiteSpace(configuredPath) ? "App_Data" : configuredPath;
        return Path.IsPathRooted(raw) ? raw : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), raw));
    }
}
