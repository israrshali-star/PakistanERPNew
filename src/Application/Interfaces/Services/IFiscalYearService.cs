using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IFiscalYearService
{
    Task<DataTableResponse<FiscalYearListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<FiscalYearDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<FiscalYearSaveResult> CreateAsync(
        FiscalYearSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<FiscalYearSaveResult> UpdateAsync(
        FiscalYearSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<FiscalYearActionResult> SetActiveAsync(int id, CancellationToken cancellationToken = default);

    Task<FiscalYearActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
