using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IDatabaseBackupService
{
    Task<DataTableResponse<DatabaseBackupHistoryListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<JobActionResult> RunBackupAsync(
        JobRunType runType,
        CancellationToken cancellationToken = default);

    Task<(byte[] Content, string FileName)?> DownloadAsync(int id, CancellationToken cancellationToken = default);

    Task<JobActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task CleanupRetentionAsync(CancellationToken cancellationToken = default);
}
