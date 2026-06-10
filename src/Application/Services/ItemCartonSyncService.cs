using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class ItemCartonSyncService : IItemCartonSyncService
{
    private readonly IUnitOfWork _unitOfWork;

    public ItemCartonSyncService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task SyncCompanyItemsAsync(int companyId, CancellationToken cancellationToken = default) =>
        SyncItemsAsync(companyId, itemIds: null, cancellationToken);

    public async Task SyncItemsAsync(
        int companyId,
        IEnumerable<int>? itemIds,
        CancellationToken cancellationToken = default)
    {
        var itemIdSet = itemIds?.Distinct().ToHashSet();

        var itemsQuery = _unitOfWork.Repository<Item>()
            .Query(asNoTracking: false)
            .Where(i => i.CompanyId == companyId);

        if (itemIdSet is { Count: > 0 })
        {
            itemsQuery = itemsQuery.Where(i => itemIdSet.Contains(i.Id));
        }

        var items = await itemsQuery.ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return;
        }

        var targetItemIds = items.Select(i => i.Id).ToList();
        var purchaseLines = await _unitOfWork.Repository<VendorBillLine>()
            .Query()
            .Where(l => l.ItemId.HasValue
                        && targetItemIds.Contains(l.ItemId.Value)
                        && l.VendorBill.CompanyId == companyId
                        && l.VendorBill.Status == BillStatus.Approved)
            .Select(l => new
            {
                ItemId = l.ItemId!.Value,
                l.LotNo,
                l.Cartons
            })
            .ToListAsync(cancellationToken);

        var salesLines = await _unitOfWork.Repository<SalesInvoiceLine>()
            .Query()
            .Where(l => targetItemIds.Contains(l.ItemId)
                        && l.SalesInvoice.CompanyId == companyId
                        && l.SalesInvoice.Status == InvoiceStatus.Posted)
            .Select(l => new
            {
                l.ItemId,
                l.LotNo,
                l.Cartons,
                l.SalesInvoice.InvoiceType
            })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var changed = false;
        foreach (var item in items)
        {
            var itemLot = NormalizeLot(item.LotNo);
            var purchased = purchaseLines
                .Where(l => l.ItemId == item.Id && NormalizeLot(l.LotNo) == itemLot)
                .Sum(l => Math.Round(l.Cartons, 2));
            var sold = salesLines
                .Where(l => l.ItemId == item.Id && NormalizeLot(l.LotNo) == itemLot)
                .Sum(l => Math.Round(
                    l.InvoiceType == InvoiceType.CreditNote ? -l.Cartons : l.Cartons,
                    2));

            var rounded = Math.Round(purchased - sold, 2);
            if (item.Cartons == rounded)
            {
                continue;
            }

            item.Cartons = rounded;
            item.UpdatedAt = now;
            _unitOfWork.Repository<Item>().Update(item);
            changed = true;
        }

        if (changed)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    internal static string NormalizeLot(string? lotNo) =>
        (lotNo ?? string.Empty).Trim();
}
