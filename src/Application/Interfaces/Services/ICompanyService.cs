using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Company management and user-company access.
/// </summary>
public interface ICompanyService
{
    Task<IReadOnlyList<CompanyDto>> GetUserCompaniesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanyListItemDto>> GetManageableCompaniesAsync(CancellationToken cancellationToken = default);
    Task<CompanyDto?> GetCurrentCompanyAsync(CancellationToken cancellationToken = default);
    Task<CompanyDetailDto?> GetCompanyDetailAsync(int companyId, CancellationToken cancellationToken = default);
    Task<bool> SetCurrentCompanyAsync(int companyId, CancellationToken cancellationToken = default);
    Task<CompanySaveResult> CreateCompanyAsync(CompanySaveRequest request, CancellationToken cancellationToken = default);
    Task<CompanySaveResult> UpdateCompanyAsync(CompanySaveRequest request, CancellationToken cancellationToken = default);
    Task<CompanySaveResult> DeleteCompanyAsync(int companyId, CancellationToken cancellationToken = default);
    Task<CompanySaveResult> SetDefaultCompanyAsync(int companyId, CancellationToken cancellationToken = default);
}
