using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Read-only global lookup data for dropdowns and forms.
/// </summary>
public interface ILookupService
{
    Task<IReadOnlyList<LookupDto>> GetProvincesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupDto>> GetUnitsOfMeasureAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScenarioTypeDto>> GetScenarioTypesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountTypeDto>> GetAccountTypesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubAccountTypeDto>> GetSubAccountTypesAsync(int? typeId = null, CancellationToken cancellationToken = default);
}
