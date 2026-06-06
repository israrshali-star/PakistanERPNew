using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Company management and user-company access.
/// </summary>
public interface ICompanyService
{
    Task<IReadOnlyList<CompanyDto>> GetUserCompaniesAsync(CancellationToken cancellationToken = default);
    Task<CompanyDto?> GetCurrentCompanyAsync(CancellationToken cancellationToken = default);
    Task<bool> SetCurrentCompanyAsync(int companyId, CancellationToken cancellationToken = default);
}
