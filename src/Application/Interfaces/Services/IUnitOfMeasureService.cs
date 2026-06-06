using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IUnitOfMeasureService
{
    Task<DataTableResponse<UnitOfMeasureListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<UnitOfMeasureDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<UnitOfMeasureSaveResult> CreateAsync(
        UnitOfMeasureSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<UnitOfMeasureSaveResult> UpdateAsync(
        UnitOfMeasureSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<UnitOfMeasureSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
