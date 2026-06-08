namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ICompanyDataPurgeService
{
    /// <summary>
    /// Hard-deletes all business data for a company while keeping the company record and user access.
    /// </summary>
    Task<CompanyDataPurgeResult> PurgeAsync(int companyId, CancellationToken cancellationToken = default);
}

public sealed record CompanyDataPurgeResult(
    bool Success,
    string Message,
    int RowsDeleted);
