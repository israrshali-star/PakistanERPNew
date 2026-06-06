using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IItemService
{
    Task<DataTableResponse<ItemListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<ItemDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<NextItemCodeDto> GenerateNextItemCodeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ItemCategoryLookupDto>> GetCategoryLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<ItemSaveResult> CreateAsync(ItemSaveRequest request, CancellationToken cancellationToken = default);

    Task<ItemSaveResult> UpdateAsync(ItemSaveRequest request, CancellationToken cancellationToken = default);

    Task<ItemSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
