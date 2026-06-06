using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ICompanySettingsService
{
    Task<CompanySettingsDto?> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<CompanySettingsSaveResult> UpdateSettingsAsync(
        CompanySettingsSaveRequest request,
        CancellationToken cancellationToken = default);
}
