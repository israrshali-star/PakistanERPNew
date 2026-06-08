using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ICustomReportService
{
    Task<IReadOnlyList<CustomReportSourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default);

    Task<CustomReportRunResult> RunAsync(
        CustomReportRunRequest request,
        CancellationToken cancellationToken = default);

    Task<byte[]> ExportToExcelAsync(
        CustomReportRunRequest request,
        CancellationToken cancellationToken = default);
}
