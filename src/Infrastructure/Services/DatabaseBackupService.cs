using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using PakistanAccountingERP.Infrastructure.Options;

namespace PakistanAccountingERP.Infrastructure.Services;

public class DatabaseBackupService : IDatabaseBackupService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly string _connectionString;
    private readonly BackupOptions _options;
    private readonly ILogger<DatabaseBackupService> _logger;

    public DatabaseBackupService(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IConfiguration configuration,
        IOptions<BackupOptions> options,
        ILogger<DatabaseBackupService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured.");
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DataTableResponse<DatabaseBackupHistoryListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<DatabaseBackupHistory>().Query();
        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim().ToLower();
            query = query.Where(x =>
                x.FileName.ToLower().Contains(term)
                || x.RunType.ToString().ToLower().Contains(term)
                || x.Status.ToString().ToLower().Contains(term)
                || (x.ErrorMessage != null && x.ErrorMessage.ToLower().Contains(term))
                || (x.CreatedBy != null && x.CreatedBy.ToLower().Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);
        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(x => new DatabaseBackupHistoryListItemDto(
                x.Id,
                x.FileName,
                x.FileSizeBytes,
                x.RunType,
                x.Status,
                x.StartedAt,
                x.CompletedAt,
                x.ErrorMessage,
                x.CreatedBy))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<DatabaseBackupHistoryListItemDto>(request.Draw, recordsTotal, recordsFiltered, rows);
    }

    public async Task<JobActionResult> RunBackupAsync(
        JobRunType runType,
        BackupDestination destination = BackupDestination.Online,
        CancellationToken cancellationToken = default)
    {
        var userName = _currentUser.UserName ?? "system";
        var startedAt = DateTime.UtcNow;

        DatabaseBackupHistory history;
        string filePath;
        string dbName;
        try
        {
            dbName = GetDatabaseName();
            var fileName = $"{dbName}_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
            var appStorageDirectory = GetAppStorageDirectory();
            Directory.CreateDirectory(appStorageDirectory);
            filePath = Path.Combine(appStorageDirectory, fileName);

            history = new DatabaseBackupHistory
            {
                FileName = fileName,
                FilePath = filePath,
                FileSizeBytes = 0,
                RunType = runType,
                Status = JobRunStatus.Running,
                StartedAt = startedAt,
                CreatedAt = startedAt,
                CreatedBy = userName
            };

            await _unitOfWork.Repository<DatabaseBackupHistory>().AddAsync(history, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database backup could not be initialized.");
            return new JobActionResult(false, "Backup failed: " + ex.Message, null);
        }

        try
        {
            await BackupDatabaseToDiskAsync(dbName, filePath, cancellationToken);
            history.FileSizeBytes = new FileInfo(filePath).Length;
            history.Status = JobRunStatus.Completed;
            history.CompletedAt = DateTime.UtcNow;
            history.UpdatedAt = DateTime.UtcNow;
            history.UpdatedBy = userName;
            _unitOfWork.Repository<DatabaseBackupHistory>().Update(history);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await CleanupRetentionAsync(cancellationToken);

            var message = destination == BackupDestination.Local
                ? "Database backup completed. Your browser will download the file — choose where to save it on this computer."
                : "Database backup completed and saved on the server.";

            return new JobActionResult(true, message, history.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database backup failed.");
            history.Status = JobRunStatus.Failed;
            history.ErrorMessage = ex.Message;
            history.CompletedAt = DateTime.UtcNow;
            history.UpdatedAt = DateTime.UtcNow;
            history.UpdatedBy = userName;
            _unitOfWork.Repository<DatabaseBackupHistory>().Update(history);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return new JobActionResult(false, "Backup failed: " + ex.Message, history.Id);
        }
    }

    public async Task<(byte[] Content, string FileName)?> DownloadAsync(int id, CancellationToken cancellationToken = default)
    {
        var row = await _unitOfWork.Repository<DatabaseBackupHistory>()
            .Query()
            .Where(x => x.Id == id)
            .Select(x => new { x.FilePath, x.FileName })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null || !File.Exists(row.FilePath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(row.FilePath, cancellationToken);
        return (bytes, row.FileName);
    }

    public async Task<JobActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<DatabaseBackupHistory>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
        {
            return new JobActionResult(false, "Backup record not found.");
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(entity.FilePath) && File.Exists(entity.FilePath))
            {
                File.Delete(entity.FilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete backup file {Path}", entity.FilePath);
        }

        _unitOfWork.Repository<DatabaseBackupHistory>().Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new JobActionResult(true, "Backup record deleted.");
    }

    public async Task CleanupRetentionAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = Math.Max(1, _options.RetentionDays);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var oldRecords = await _unitOfWork.Repository<DatabaseBackupHistory>()
            .Query(asNoTracking: false)
            .Where(x => x.StartedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (oldRecords.Count == 0)
        {
            return;
        }

        foreach (var record in oldRecords)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(record.FilePath) && File.Exists(record.FilePath))
                {
                    File.Delete(record.FilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed deleting old backup file {Path}", record.FilePath);
            }
        }

        _unitOfWork.Repository<DatabaseBackupHistory>().RemoveRange(oldRecords);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private string GetDatabaseName()
    {
        var builder = new SqlConnectionStringBuilder(_connectionString);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            throw new InvalidOperationException("Database name missing in connection string.");
        }

        return builder.InitialCatalog;
    }

    private string GetAppStorageDirectory()
    {
        string path;
        if (!string.IsNullOrWhiteSpace(_options.StoragePath))
        {
            var raw = _options.StoragePath.Trim();
            path = Path.IsPathRooted(raw)
                ? raw
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), raw));
        }
        else
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "PakistanAccountingERP",
                "Backups");
        }

        // SQL Server cannot write under bin/App_Data; use a shared folder both SQL and the app can access.
        if (path.Contains("App_Data", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "PakistanAccountingERP",
                "Backups");
        }

        return path;
    }

    private async Task BackupDatabaseToDiskAsync(
        string dbName,
        string filePath,
        CancellationToken cancellationToken)
    {
        var targetDirectory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Backup file path is invalid.");
        Directory.CreateDirectory(targetDirectory);

        await ExecuteBackupAsync(dbName, filePath, cancellationToken);
        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException($"Backup file was not created at {filePath}.");
        }
    }

    private async Task ExecuteBackupAsync(string dbName, string filePath, CancellationToken cancellationToken)
    {
        var sql = $"BACKUP DATABASE [{dbName}] TO DISK = @backupPath WITH INIT, COMPRESSION, STATS = 10;";
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@backupPath", filePath);
        command.CommandTimeout = 0;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IQueryable<DatabaseBackupHistory> ApplyOrdering(
        IQueryable<DatabaseBackupHistory> query,
        DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(x => x.FileName) : query.OrderBy(x => x.FileName),
            1 => desc ? query.OrderByDescending(x => x.FileSizeBytes) : query.OrderBy(x => x.FileSizeBytes),
            2 => desc ? query.OrderByDescending(x => x.RunType) : query.OrderBy(x => x.RunType),
            3 => desc ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            4 => desc ? query.OrderByDescending(x => x.StartedAt) : query.OrderBy(x => x.StartedAt),
            5 => desc ? query.OrderByDescending(x => x.CompletedAt) : query.OrderBy(x => x.CompletedAt),
            _ => query.OrderByDescending(x => x.StartedAt)
        };
    }
}
