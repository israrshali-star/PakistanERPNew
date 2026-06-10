namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IItemCartonSyncService
{
    Task SyncCompanyItemsAsync(int companyId, CancellationToken cancellationToken = default);

    Task SyncItemsAsync(
        int companyId,
        IEnumerable<int>? itemIds,
        CancellationToken cancellationToken = default);
}
