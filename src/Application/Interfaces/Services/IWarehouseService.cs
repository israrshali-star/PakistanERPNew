using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IWarehouseService
{
    Task<DataTableResponse<WarehouseListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<WarehouseDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<NextWarehouseCodeDto> GenerateNextCodeAsync(CancellationToken cancellationToken = default);

    Task<WarehouseSaveResult> CreateAsync(
        WarehouseSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<WarehouseSaveResult> UpdateAsync(
        WarehouseSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<WarehouseSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
