using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IQuickBooksIifImportService
{
    Task<QuickBooksIifImportResult> ImportAsync(
        string filePath,
        int companyId,
        QuickBooksIifImportOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<QuickBooksIifImportResult> ImportReportsAsync(
        int companyId,
        QuickBooksIifImportOptions options,
        CancellationToken cancellationToken = default);
}
