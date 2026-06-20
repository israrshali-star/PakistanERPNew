using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class InventoryCostingService : IInventoryCostingService
{
    private readonly IUnitOfWork _unitOfWork;

    public InventoryCostingService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<InventoryCostingBatch> CreateBatchAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        CancellationToken cancellationToken = default)
    {
        var distinctItemIds = itemIds.Distinct().ToList();
        var purchaseRatesByItem = distinctItemIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await _unitOfWork.Repository<Item>()
                .Query()
                .Where(i => i.CompanyId == companyId && distinctItemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.PurchaseRate, cancellationToken);

        var layersByItem = await BuildRemainingLayersAsync(
            companyId,
            distinctItemIds,
            purchaseRatesByItem,
            cancellationToken);
        var stackLotRates = await BuildStackLotPurchaseRatesAsync(
            companyId,
            distinctItemIds,
            purchaseRatesByItem,
            cancellationToken);

        return new InventoryCostingBatch(request => CalculateLineCost(request, layersByItem, stackLotRates));
    }

    private static InventoryLineCostResult CalculateLineCost(
        InventoryLineCostRequest request,
        Dictionary<int, List<CostLayer>> layersByItem,
        Dictionary<StackLotRateKey, decimal> stackLotRates)
    {
        var quantity = Math.Round(request.Quantity, 2);
        if (quantity <= 0m)
        {
            return InventoryLineCostResult.Ok(0m, 0m);
        }

        var resolvedStack = ResolveStackLot(request.StackNo, request.ItemStackNo);
        var resolvedLot = ResolveStackLot(request.LotNo, request.ItemLotNo);
        var fallbackRate = Math.Round(request.FallbackPurchaseRate, 2);

        if (!layersByItem.TryGetValue(request.ItemId, out var layers))
        {
            layers = [];
            layersByItem[request.ItemId] = layers;
        }

        var eligibleLayers = layers
            .Where(l => l.RemainingQty > 0m && MatchesStackLot(l, resolvedStack, resolvedLot))
            .ToList();

        if (eligibleLayers.Count == 0)
        {
            var rate = ResolveFallbackRate(stackLotRates, request, resolvedStack, resolvedLot, fallbackRate);
            return InventoryLineCostResult.Ok(rate, Math.Round(quantity * rate, 2));
        }

        if (request.CostingMethod == CostingMethod.AverageCosting)
        {
            return ConsumeAverageCost(eligibleLayers, quantity, fallbackRate, stackLotRates, request, resolvedStack, resolvedLot);
        }

        return ConsumeFifoCost(eligibleLayers, quantity, fallbackRate, stackLotRates, request, resolvedStack, resolvedLot);
    }

    private static InventoryLineCostResult ConsumeFifoCost(
        List<CostLayer> eligibleLayers,
        decimal quantity,
        decimal fallbackRate,
        Dictionary<StackLotRateKey, decimal> stackLotRates,
        InventoryLineCostRequest request,
        string? resolvedStack,
        string? resolvedLot)
    {
        var remaining = quantity;
        var totalCost = 0m;

        foreach (var layer in eligibleLayers.OrderBy(l => l.TransactionDate).ThenBy(l => l.TransactionId))
        {
            if (remaining <= 0m)
            {
                break;
            }

            var take = Math.Min(remaining, layer.RemainingQty);
            var layerRate = EffectiveLayerRate(layer.UnitCost, fallbackRate, stackLotRates, request, resolvedStack, resolvedLot);
            totalCost += Math.Round(take * layerRate, 2);
            layer.RemainingQty = Math.Round(layer.RemainingQty - take, 2);
            remaining = Math.Round(remaining - take, 2);
        }

        if (remaining > 0m)
        {
            var rate = ResolveFallbackRate(stackLotRates, request, resolvedStack, resolvedLot, fallbackRate);
            totalCost += Math.Round(remaining * rate, 2);
        }

        totalCost = Math.Round(totalCost, 2);
        var unitCost = quantity > 0m ? Math.Round(totalCost / quantity, 2) : 0m;
        return InventoryLineCostResult.Ok(unitCost, totalCost);
    }

    private static InventoryLineCostResult ConsumeAverageCost(
        List<CostLayer> eligibleLayers,
        decimal quantity,
        decimal fallbackRate,
        Dictionary<StackLotRateKey, decimal> stackLotRates,
        InventoryLineCostRequest request,
        string? resolvedStack,
        string? resolvedLot)
    {
        var availableQty = eligibleLayers.Sum(l => l.RemainingQty);
        if (availableQty <= 0m)
        {
            var rate = ResolveFallbackRate(stackLotRates, request, resolvedStack, resolvedLot, fallbackRate);
            return InventoryLineCostResult.Ok(rate, Math.Round(quantity * rate, 2));
        }

        var layerValue = eligibleLayers.Sum(l => Math.Round(
            l.RemainingQty * EffectiveLayerRate(l.UnitCost, fallbackRate, stackLotRates, request, resolvedStack, resolvedLot),
            2));
        var averageRate = Math.Round(layerValue / availableQty, 2);
        var totalCost = Math.Round(quantity * averageRate, 2);

        var remaining = quantity;
        foreach (var layer in eligibleLayers.OrderBy(l => l.TransactionDate).ThenBy(l => l.TransactionId))
        {
            if (remaining <= 0m)
            {
                break;
            }

            var take = Math.Min(remaining, layer.RemainingQty);
            layer.RemainingQty = Math.Round(layer.RemainingQty - take, 2);
            remaining = Math.Round(remaining - take, 2);
        }

        if (remaining > 0m)
        {
            var rate = ResolveFallbackRate(stackLotRates, request, resolvedStack, resolvedLot, fallbackRate);
            totalCost += Math.Round(remaining * rate, 2);
        }

        totalCost = Math.Round(totalCost, 2);
        var unitCost = quantity > 0m ? Math.Round(totalCost / quantity, 2) : 0m;
        return InventoryLineCostResult.Ok(unitCost, totalCost);
    }

    private static decimal EffectiveLayerRate(
        decimal layerUnitCost,
        decimal fallbackRate,
        Dictionary<StackLotRateKey, decimal> stackLotRates,
        InventoryLineCostRequest request,
        string? resolvedStack,
        string? resolvedLot)
    {
        if (layerUnitCost > 0m)
        {
            return Math.Round(layerUnitCost, 2);
        }

        return ResolveFallbackRate(stackLotRates, request, resolvedStack, resolvedLot, fallbackRate);
    }

    private static decimal ResolveFallbackRate(
        Dictionary<StackLotRateKey, decimal> stackLotRates,
        InventoryLineCostRequest request,
        string? resolvedStack,
        string? resolvedLot,
        decimal fallbackRate)
    {
        if (stackLotRates.TryGetValue(
                StackLotRateKey.From(request.ItemId, resolvedStack, resolvedLot),
                out var stackLotRate)
            && stackLotRate > 0m)
        {
            return Math.Round(stackLotRate, 2);
        }

        return Math.Round(fallbackRate, 2);
    }

    private async Task<Dictionary<int, List<CostLayer>>> BuildRemainingLayersAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        IReadOnlyDictionary<int, decimal> purchaseRatesByItem,
        CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return new Dictionary<int, List<CostLayer>>();
        }

        var transactions = await _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId && itemIds.Contains(t.ItemId))
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.Id)
            .Select(t => new TransactionRow(
                t.Id,
                t.ItemId,
                t.TransactionType,
                t.StackNo,
                t.LotNo,
                t.Item.StackNo,
                t.Item.LotNo,
                t.Quantity,
                t.UnitCost,
                t.TransactionDate))
            .ToListAsync(cancellationToken);

        var layersByItem = new Dictionary<int, List<CostLayer>>();

        foreach (var transaction in transactions)
        {
            if (!layersByItem.TryGetValue(transaction.ItemId, out var layers))
            {
                layers = [];
                layersByItem[transaction.ItemId] = layers;
            }

            var stackNo = ResolveStackLot(transaction.StackNo, transaction.ItemStackNo);
            var lotNo = ResolveStackLot(transaction.LotNo, transaction.ItemLotNo);

            switch (transaction.TransactionType)
            {
                case InventoryTransactionType.StockIn:
                case InventoryTransactionType.Opening:
                {
                    var unitCost = Math.Round(transaction.UnitCost, 2);
                    if (unitCost <= 0m
                        && purchaseRatesByItem.TryGetValue(transaction.ItemId, out var purchaseRate)
                        && purchaseRate > 0m)
                    {
                        unitCost = Math.Round(purchaseRate, 2);
                    }

                    layers.Add(new CostLayer
                    {
                        TransactionId = transaction.Id,
                        TransactionDate = transaction.TransactionDate,
                        StackNo = stackNo,
                        LotNo = lotNo,
                        RemainingQty = Math.Round(transaction.Quantity, 2),
                        UnitCost = unitCost
                    });
                    break;
                }

                case InventoryTransactionType.StockOut:
                    ConsumeLayers(
                        layers,
                        Math.Round(transaction.Quantity, 2),
                        stackNo,
                        lotNo,
                        CostingMethod.FIFO);
                    break;

                case InventoryTransactionType.Adjustment:
                    if (transaction.Quantity > 0m)
                    {
                        var unitCost = Math.Round(transaction.UnitCost, 2);
                        if (unitCost <= 0m
                            && purchaseRatesByItem.TryGetValue(transaction.ItemId, out var purchaseRate)
                            && purchaseRate > 0m)
                        {
                            unitCost = Math.Round(purchaseRate, 2);
                        }

                        layers.Add(new CostLayer
                        {
                            TransactionId = transaction.Id,
                            TransactionDate = transaction.TransactionDate,
                            StackNo = stackNo,
                            LotNo = lotNo,
                            RemainingQty = Math.Round(transaction.Quantity, 2),
                            UnitCost = unitCost
                        });
                    }
                    else if (transaction.Quantity < 0m)
                    {
                        ConsumeLayers(
                            layers,
                            Math.Round(Math.Abs(transaction.Quantity), 2),
                            stackNo,
                            lotNo,
                            CostingMethod.FIFO);
                    }
                    break;
            }
        }

        return layersByItem;
    }

    private static void ConsumeLayers(
        List<CostLayer> layers,
        decimal quantity,
        string? stackNo,
        string? lotNo,
        CostingMethod method)
    {
        var remaining = quantity;
        var eligibleLayers = layers
            .Where(l => l.RemainingQty > 0m && MatchesStackLot(l, stackNo, lotNo))
            .ToList();

        if (eligibleLayers.Count == 0)
        {
            eligibleLayers = layers.Where(l => l.RemainingQty > 0m).ToList();
        }

        if (method == CostingMethod.AverageCosting)
        {
            var availableQty = eligibleLayers.Sum(l => l.RemainingQty);
            if (availableQty <= 0m)
            {
                return;
            }

            foreach (var layer in eligibleLayers.OrderBy(l => l.TransactionDate).ThenBy(l => l.TransactionId))
            {
                if (remaining <= 0m)
                {
                    break;
                }

                var take = Math.Min(remaining, layer.RemainingQty);
                layer.RemainingQty = Math.Round(layer.RemainingQty - take, 2);
                remaining = Math.Round(remaining - take, 2);
            }

            return;
        }

        foreach (var layer in eligibleLayers.OrderBy(l => l.TransactionDate).ThenBy(l => l.TransactionId))
        {
            if (remaining <= 0m)
            {
                break;
            }

            var take = Math.Min(remaining, layer.RemainingQty);
            layer.RemainingQty = Math.Round(layer.RemainingQty - take, 2);
            remaining = Math.Round(remaining - take, 2);
        }
    }

    private async Task<Dictionary<StackLotRateKey, decimal>> BuildStackLotPurchaseRatesAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        IReadOnlyDictionary<int, decimal> purchaseRatesByItem,
        CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return new Dictionary<StackLotRateKey, decimal>();
        }

        var purchaseLines = await _unitOfWork.Repository<VendorBillLine>()
            .Query()
            .Where(l => l.VendorBill.CompanyId == companyId
                        && l.VendorBill.Status == BillStatus.Approved
                        && l.ItemId != null
                        && itemIds.Contains(l.ItemId.Value))
            .Select(l => new PurchaseRateRow(
                l.ItemId!.Value,
                l.StackNo,
                l.LotNo,
                l.Item!.StackNo,
                l.Item.LotNo,
                l.Quantity,
                l.Rate))
            .ToListAsync(cancellationToken);

        var grouped = purchaseLines
            .Select(row => row with
            {
                StackNo = ResolveStackLot(row.StackNo, row.ItemStackNo),
                LotNo = ResolveStackLot(row.LotNo, row.ItemLotNo)
            })
            .GroupBy(row => StackLotRateKey.From(row.ItemId, row.StackNo, row.LotNo));

        var rates = new Dictionary<StackLotRateKey, decimal>();
        foreach (var group in grouped)
        {
            var totalQty = group.Sum(x => x.Quantity);
            if (totalQty <= 0m)
            {
                continue;
            }

            var ratedLines = group.Where(x => x.Rate > 0m).ToList();
            decimal weightedRate;
            if (ratedLines.Count > 0)
            {
                var ratedQty = ratedLines.Sum(x => x.Quantity);
                weightedRate = ratedQty > 0m
                    ? ratedLines.Sum(x => x.Quantity * x.Rate) / ratedQty
                    : 0m;
            }
            else
            {
                weightedRate = 0m;
            }

            if (weightedRate <= 0m
                && purchaseRatesByItem.TryGetValue(group.Key.ItemId, out var purchaseRate)
                && purchaseRate > 0m)
            {
                weightedRate = purchaseRate;
            }

            if (weightedRate > 0m)
            {
                rates[group.Key] = Math.Round(weightedRate, 2);
            }
        }

        return rates;
    }

    private static bool MatchesStackLot(CostLayer layer, string? stackNo, string? lotNo)
    {
        if (!string.IsNullOrWhiteSpace(stackNo)
            && !string.Equals(layer.StackNo, stackNo, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(lotNo)
            && !string.Equals(layer.LotNo, lotNo, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string? ResolveStackLot(string? lineValue, string? itemValue)
    {
        if (!string.IsNullOrWhiteSpace(lineValue))
        {
            return lineValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(itemValue))
        {
            return itemValue.Trim();
        }

        return null;
    }

    private sealed class CostLayer
    {
        public int TransactionId { get; init; }
        public DateTime TransactionDate { get; init; }
        public string? StackNo { get; init; }
        public string? LotNo { get; init; }
        public decimal RemainingQty { get; set; }
        public decimal UnitCost { get; init; }
    }

    private sealed record TransactionRow(
        int Id,
        int ItemId,
        InventoryTransactionType TransactionType,
        string? StackNo,
        string? LotNo,
        string ItemStackNo,
        string ItemLotNo,
        decimal Quantity,
        decimal UnitCost,
        DateTime TransactionDate);

    private sealed record PurchaseRateRow(
        int ItemId,
        string? StackNo,
        string? LotNo,
        string ItemStackNo,
        string ItemLotNo,
        decimal Quantity,
        decimal Rate);

    private sealed record StackLotRateKey(int ItemId, string? StackNo, string? LotNo)
    {
        public static StackLotRateKey From(int itemId, string? stackNo, string? lotNo) =>
            new(itemId, NormalizeKeyPart(stackNo), NormalizeKeyPart(lotNo));

        private static string? NormalizeKeyPart(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }
}
