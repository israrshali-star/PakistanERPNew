namespace PakistanAccountingERP.Application.Interfaces.Services;

public sealed record CustomerExcelImportResult(
    bool Success,
    string Message,
    int Imported = 0,
    int Skipped = 0,
    int Updated = 0);

public interface ICustomerExcelImportService
{
    Task<CustomerExcelImportResult> ImportAsync(
        string filePath,
        int companyId,
        bool updateExisting = false,
        CancellationToken cancellationToken = default);

    Task<CustomerExcelImportResult> FixDuplicateNameInAddressesAsync(
        int companyId,
        CancellationToken cancellationToken = default);
}
