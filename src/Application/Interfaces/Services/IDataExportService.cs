using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IDataExportService
{
    Task<IReadOnlyList<object>> GetExportTypesAsync(CancellationToken cancellationToken = default);

    Task<DataTableResponse<DataExportHistoryListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<JobActionResult> RunExportAsync(
        DataExportType exportType,
        CancellationToken cancellationToken = default);

    Task<(byte[] Content, string FileName)?> DownloadAsync(int id, CancellationToken cancellationToken = default);

    Task<JobActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
