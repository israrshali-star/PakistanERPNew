using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common.Constants;
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
        var onHandStacks = await BuildOnHandStacksAsync(companyId, targetItemIds, cancellationToken);
        var purchaseCartonsByStack = await BuildPurchaseCartonsByStackAsync(companyId, targetItemIds, cancellationToken);
        var salesCartonsByStack = await BuildSalesCartonsByStackAsync(companyId, targetItemIds, cancellationToken);

        var now = DateTime.UtcNow;
        var changed = false;
        foreach (var item in items)
        {
            var stacks = onHandStacks
                .Where(s => s.ItemId == item.Id)
                .ToList();

            var cartons = Math.Round(stacks.Sum(stack =>
            {
                var key = StackKey(stack.ItemId, stack.StackNo);
                var purchased = purchaseCartonsByStack.GetValueOrDefault(key);
                var sold = salesCartonsByStack.GetValueOrDefault(key);
                return Math.Round(purchased - sold, 2);
            }), 2);

            if (cartons < 0m)
            {
                cartons = 0m;
            }

            string? stackNo = null;
            if (stacks.Count == 1)
            {
                stackNo = string.IsNullOrWhiteSpace(stacks[0].StackNo) ? null : stacks[0].StackNo.Trim();
            }

            var resolvedStackNo = stackNo ?? string.Empty;
            var stackChanged = !string.Equals(item.StackNo, resolvedStackNo, StringComparison.Ordinal);

            if (item.Cartons == cartons && !stackChanged)
            {
                continue;
            }

            item.Cartons = cartons;
            item.StackNo = resolvedStackNo;
            item.UpdatedAt = now;
            _unitOfWork.Repository<Item>().Update(item);
            changed = true;
        }

        if (changed)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<List<OnHandStack>> BuildOnHandStacksAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        CancellationToken cancellationToken)
    {
        var rows = await _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId && itemIds.Contains(t.ItemId))
            .GroupBy(t => new { t.ItemId, StackNo = t.StackNo ?? string.Empty })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.StackNo,
                NetQuantity = g.Sum(t =>
                    t.TransactionType == InventoryTransactionType.StockOut
                        ? -t.Quantity
                        : t.Quantity)
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(x => x.NetQuantity > 0.01m || x.NetQuantity < -0.01m)
            .Select(x => new OnHandStack(x.ItemId, x.StackNo, x.NetQuantity))
            .ToList();
    }

    private async Task<Dictionary<string, decimal>> BuildPurchaseCartonsByStackAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        CancellationToken cancellationToken)
    {
        var lines = await _unitOfWork.Repository<VendorBillLine>()
            .Query()
            .Where(l => l.ItemId.HasValue
                        && itemIds.Contains(l.ItemId.Value)
                        && l.VendorBill.CompanyId == companyId
                        && (l.VendorBill.Status == BillStatus.Approved
                            || l.VendorBill.BillNumber == AppConstants.OpeningStockBillNumber))
            .Select(l => new
            {
                ItemId = l.ItemId!.Value,
                StackNo = l.StackNo ?? string.Empty,
                l.Cartons
            })
            .ToListAsync(cancellationToken);

        return lines
            .GroupBy(l => StackKey(l.ItemId, l.StackNo))
            .ToDictionary(g => g.Key, g => Math.Round(g.Sum(x => x.Cartons), 2));
    }

    private async Task<Dictionary<string, decimal>> BuildSalesCartonsByStackAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        CancellationToken cancellationToken)
    {
        var lines = await _unitOfWork.Repository<SalesInvoiceLine>()
            .Query()
            .Where(l => itemIds.Contains(l.ItemId)
                        && l.SalesInvoice.CompanyId == companyId
                        && l.SalesInvoice.Status == InvoiceStatus.Posted)
            .Select(l => new
            {
                l.ItemId,
                StackNo = l.StackNo ?? string.Empty,
                l.Cartons,
                l.SalesInvoice.InvoiceType
            })
            .ToListAsync(cancellationToken);

        return lines
            .GroupBy(l => StackKey(l.ItemId, l.StackNo))
            .ToDictionary(
                g => g.Key,
                g => Math.Round(g.Sum(x =>
                    x.InvoiceType == InvoiceType.CreditNote ? -x.Cartons : x.Cartons), 2));
    }

    private static string StackKey(int itemId, string? stackNo) =>
        $"{itemId}|{(stackNo ?? string.Empty).Trim()}";

    private sealed record OnHandStack(int ItemId, string StackNo, decimal NetQuantity);
}
