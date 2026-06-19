namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IItemCopyService
{
    Task<ItemCopyResult> CopyItemsAsync(
        int sourceCompanyId,
        IReadOnlyList<int> targetCompanyIds,
        CancellationToken cancellationToken = default);
}

public record ItemCopyResult(
    bool Success,
    string Message,
    int CategoriesCreated,
    int ItemsCreated,
    int ItemsSkipped);
