using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IItemCategoryService
{
    Task<DataTableResponse<ItemCategoryListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<ItemCategoryDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<ItemCategorySaveResult> CreateAsync(
        ItemCategorySaveRequest request,
        CancellationToken cancellationToken = default);

    Task<ItemCategorySaveResult> UpdateAsync(
        ItemCategorySaveRequest request,
        CancellationToken cancellationToken = default);

    Task<ItemCategorySaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
