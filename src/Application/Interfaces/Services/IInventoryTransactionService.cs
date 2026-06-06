using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IInventoryTransactionService
{
    Task<DataTableResponse<InventoryTransactionListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<InventoryTransactionDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<NextStockReferenceDto> GenerateNextReferenceAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryItemLookupDto>> GetItemLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryWarehouseLookupDto>> GetWarehouseLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<InventoryTransactionSaveResult> CreateAsync(
        InventoryTransactionSaveRequest request,
        CancellationToken cancellationToken = default);
}
