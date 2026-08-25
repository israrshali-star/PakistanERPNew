using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class InventoryReportService : IInventoryReportService
{
    private const string OpeningStockRefNo = "OPENING-31MAY2026";

    private const string OpeningStockBillNumber = AppConstants.OpeningStockBillNumber;

    private static readonly DateTime OpeningStockBillDate = new(2026, 5, 31);

    private readonly IUnitOfWork _unitOfWork;

    private readonly ICurrentCompanyService _currentCompany;

    private readonly IItemCartonSyncService _itemCartonSyncService;

    private readonly IInventoryCostingService _inventoryCosting;

    public InventoryReportService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        IItemCartonSyncService itemCartonSyncService,
        IInventoryCostingService inventoryCosting)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _itemCartonSyncService = itemCartonSyncService;
        _inventoryCosting = inventoryCosting;
    }

    public async Task<StockSummaryReportDto> GetStockSummaryAsync(
        StockSummaryReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var asOfDate = (request.AsOfDate ?? DateTime.UtcNow).Date;
        var asOfEnd = asOfDate.AddDays(1).AddTicks(-1);
        var query = _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId);
        if (request.ActiveOnly)
        {
            query = query.Where(i => i.IsActive);
        }
        if (request.CategoryId.HasValue)
        {
            query = query.Where(i => i.ItemCategoryId == request.CategoryId.Value);
        }

        var items = await query
            .OrderBy(i => i.ItemCode)
            .Select(i => new
            {
                i.Id,
                i.ItemCode,
                i.ItemName,
                CategoryName = i.ItemCategory != null ? i.ItemCategory.Name : null,
                UnitSymbol = i.UnitOfMeasure.Symbol ?? i.UnitOfMeasure.Name,
                UnitName = i.UnitOfMeasure.Name,
                i.CurrentStock,
                i.Cartons,
                i.StackNo,
                i.LotNo,
                i.PurchaseRate,
                i.CostingMethod
            })
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return new StockSummaryReportDto(DateTime.UtcNow, asOfDate, 0, 0m, 0m, 0m, []);
        }

        var itemIds = items.Select(i => i.Id).ToList();
        var itemById = items.ToDictionary(i => i.Id);

        // One report line per item + lot; cartons/qty/value are summed across stacks in the lot.
        var stackBalances = await _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t =>
                t.CompanyId == companyId
                && itemIds.Contains(t.ItemId)
                && !t.IsDeleted
                && t.TransactionDate <= asOfEnd)
            .GroupBy(t => new
            {
                t.ItemId,
                StackNo = t.StackNo ?? string.Empty,
                LotNo = t.LotNo ?? string.Empty
            })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.StackNo,
                g.Key.LotNo,
                Quantity = g.Sum(t =>
                    t.TransactionType == InventoryTransactionType.StockOut
                        ? -t.Quantity
                        : t.Quantity)
            })
            .ToListAsync(cancellationToken);

        var vendorRefs = TradeInvoiceLayout.ShowsStockSummaryVendorRef(companyId)
            ? await BuildVendorRefLookupsAsync(
                companyId,
                itemIds,
                asOfEnd,
                cancellationToken)
            : VendorRefLookups.Empty;
        var purchaseCartonsByLot = await BuildLotPurchaseCartonsAsync(
            companyId,
            itemIds,
            asOfEnd,
            cancellationToken);
        var salesCartonsByLot = await BuildLotSalesCartonsAsync(
            companyId,
            itemIds,
            asOfEnd,
            cancellationToken);

        var itemsNeedingCost = items
            .Where(i => i.PurchaseRate <= 0m)
            .Select(i => i.Id)
            .Distinct()
            .ToList();
        var costingBatch = itemsNeedingCost.Count > 0
            ? await _inventoryCosting.CreateBatchAsync(companyId, itemsNeedingCost, cancellationToken)
            : null;

        var stackLines = new List<(
            int ItemId,
            string ItemCode,
            string ItemName,
            string? LotNo,
            string? CategoryName,
            string UnitSymbol,
            decimal Stock,
            decimal Rate,
            decimal Value)>();
        var itemsWithStackRows = new HashSet<int>();

        foreach (var s in stackBalances.Where(x => Math.Abs(x.Quantity) > 0.01m))
        {
            if (!itemById.TryGetValue(s.ItemId, out var item))
            {
                continue;
            }

            itemsWithStackRows.Add(s.ItemId);
            var stackNo = string.IsNullOrWhiteSpace(s.StackNo) ? null : s.StackNo.Trim();
            var lotNo = string.IsNullOrWhiteSpace(s.LotNo) ? null : s.LotNo.Trim();
            var stockOnHand = Math.Round(s.Quantity, 2);
            var (rate, value) = ResolveStockValuation(
                costingBatch,
                item.Id,
                stackNo,
                lotNo,
                item.StackNo,
                item.LotNo,
                stockOnHand,
                item.CostingMethod,
                item.PurchaseRate);

            stackLines.Add((
                item.Id,
                item.ItemCode,
                item.ItemName,
                lotNo,
                item.CategoryName,
                FormatUnitSymbol(item.UnitSymbol, item.UnitName),
                stockOnHand,
                rate,
                value));
        }

        var itemsWithAnyTransactions = (await _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId
                        && itemIds.Contains(t.ItemId)
                        && !t.IsDeleted)
            .Select(t => t.ItemId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        // Items with on-hand qty but no inventory transactions at all (legacy / unsynced).
        // Do not use CurrentStock for items that only moved after the as-of date.
        foreach (var item in items.Where(i => !itemsWithStackRows.Contains(i.Id)))
        {
            if (itemsWithAnyTransactions.Contains(item.Id))
            {
                continue;
            }

            if (Math.Abs(item.CurrentStock) <= 0.01m && Math.Abs(item.Cartons) <= 0.01m)
            {
                continue;
            }

            var (rate, value) = ResolveStockValuation(
                costingBatch,
                item.Id,
                stackNo: null,
                lotNo: null,
                item.StackNo,
                item.LotNo,
                item.CurrentStock,
                item.CostingMethod,
                item.PurchaseRate);

            stackLines.Add((
                item.Id,
                item.ItemCode,
                item.ItemName,
                string.IsNullOrWhiteSpace(item.LotNo) ? null : item.LotNo.Trim(),
                item.CategoryName,
                FormatUnitSymbol(item.UnitSymbol, item.UnitName),
                Math.Round(item.CurrentStock, 2),
                rate,
                value));
        }

        var lines = stackLines
            .GroupBy(l => (
                l.ItemId,
                LotKey: (string.IsNullOrWhiteSpace(l.LotNo) ? string.Empty : l.LotNo.Trim())
                    .ToUpperInvariant()))
            .Select(g =>
            {
                var first = g.First();
                var lotNo = string.IsNullOrWhiteSpace(first.LotNo) ? null : first.LotNo.Trim();
                var stock = Math.Round(g.Sum(x => x.Stock), 2);
                var value = Math.Round(g.Sum(x => x.Value), 2);
                var rate = Math.Abs(stock) > 0.01m
                    ? Math.Round(value / stock, 2)
                    : Math.Round(g.Average(x => x.Rate), 2);
                var lotKey = LotCartonKey(first.ItemId, lotNo);
                var cartons = Math.Round(
                    purchaseCartonsByLot.GetValueOrDefault(lotKey)
                    - salesCartonsByLot.GetValueOrDefault(lotKey),
                    2);
                if (cartons < 0m)
                {
                    cartons = 0m;
                }

                // Fallback for master-only rows with no purchase/sale carton history.
                if (cartons == 0m
                    && g.Count() == 1
                    && !itemsWithStackRows.Contains(first.ItemId)
                    && itemById.TryGetValue(first.ItemId, out var masterItem)
                    && masterItem.Cartons > 0m)
                {
                    cartons = Math.Round(masterItem.Cartons, 2);
                }

                return new StockSummaryLineDto(
                    first.ItemId,
                    first.ItemCode,
                    first.ItemName,
                    lotNo,
                    vendorRefs.ForLot(first.ItemId, lotNo),
                    first.CategoryName,
                    first.UnitSymbol,
                    stock,
                    cartons,
                    rate,
                    value);
            })
            .Where(l => !request.HideZeroQoh || l.CurrentStock != 0 || l.CurrentCartons != 0)
            .OrderBy(l => l.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.LotNo, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new StockSummaryReportDto(
            DateTime.UtcNow,
            asOfDate,
            lines.Count,
            lines.Sum(l => l.CurrentStock),
            lines.Sum(l => l.CurrentCartons),
            lines.Sum(l => l.StockValue),
            lines);
    }

    public async Task<StackWiseStockReportDto> GetStackWiseStockAsync(
        StockSummaryReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var asOfDate = (request.AsOfDate ?? DateTime.UtcNow).Date;
        var asOfEnd = asOfDate.AddDays(1).AddTicks(-1);
        var itemQuery = _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId);
        if (request.ActiveOnly)
        {
            itemQuery = itemQuery.Where(i => i.IsActive);
        }
        if (request.CategoryId.HasValue)
        {
            itemQuery = itemQuery.Where(i => i.ItemCategoryId == request.CategoryId.Value);
        }
        var items = await itemQuery
            .Select(i => new
            {
                i.Id,
                i.ItemCode,
                i.ItemName,
                CategoryName = i.ItemCategory != null ? i.ItemCategory.Name : null,
                i.StackNo,
                i.LotNo,
                i.PurchaseRate,
                i.CostingMethod
            })
            .ToListAsync(cancellationToken);
        var itemIds = items.Select(i => i.Id).ToList();
        if (itemIds.Count == 0)
        {
            return new StackWiseStockReportDto(
                DateTime.UtcNow,
                asOfDate,
                0,
                0m,
                0m,
                0m,
                []);
        }
        var stackBalances = await _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t =>
                t.CompanyId == companyId
                && itemIds.Contains(t.ItemId)
                && !t.IsDeleted
                && t.TransactionDate <= asOfEnd)
            .GroupBy(t => new
            {
                t.ItemId,
                StackNo = t.StackNo ?? string.Empty,
                LotNo = t.LotNo ?? string.Empty
            })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.StackNo,
                g.Key.LotNo,
                Quantity = g.Sum(t =>
                    t.TransactionType == InventoryTransactionType.StockOut
                        ? -t.Quantity
                        : t.Quantity)
            })
            .ToListAsync(cancellationToken);
        var vendorRefs = await BuildVendorRefLookupsAsync(
            companyId,
            itemIds,
            asOfEnd,
            cancellationToken);
        var purchaseCartonsByStack = await BuildStackPurchaseCartonsAsync(
            companyId,
            itemIds,
            asOfEnd,
            cancellationToken);
        var salesCartonsByStack = await BuildStackSalesCartonsAsync(
            companyId,
            itemIds,
            asOfEnd,
            cancellationToken);
        var itemById = items.ToDictionary(i => i.Id);
        var itemsNeedingCost = items
            .Where(i => i.PurchaseRate <= 0m)
            .Select(i => i.Id)
            .Distinct()
            .ToList();
        var costingBatch = itemsNeedingCost.Count > 0
            ? await _inventoryCosting.CreateBatchAsync(companyId, itemsNeedingCost, cancellationToken)
            : null;

        var lines = stackBalances
            .Where(s => Math.Abs(s.Quantity) > 0.01m)
            .Select(s =>
            {
                var item = itemById[s.ItemId];
                var stackKey = StackCartonKey(s.ItemId, s.StackNo);
                var cartons = Math.Round(
                    purchaseCartonsByStack.GetValueOrDefault(stackKey)
                    - salesCartonsByStack.GetValueOrDefault(stackKey),
                    2);
                if (cartons < 0m)
                {
                    cartons = 0m;
                }
                var quantity = Math.Round(s.Quantity, 2);
                var (rate, value) = ResolveStockValuation(
                    costingBatch,
                    item.Id,
                    string.IsNullOrWhiteSpace(s.StackNo) ? null : s.StackNo,
                    string.IsNullOrWhiteSpace(s.LotNo) ? null : s.LotNo,
                    item.StackNo,
                    item.LotNo,
                    quantity,
                    item.CostingMethod,
                    item.PurchaseRate);

                return new StackWiseStockLineDto(
                    s.ItemId,
                    item.ItemCode,
                    item.ItemName,
                    item.CategoryName,
                    string.IsNullOrWhiteSpace(s.LotNo) ? null : s.LotNo,
                    string.IsNullOrWhiteSpace(s.StackNo) ? null : s.StackNo,
                    vendorRefs.ForStack(s.ItemId, s.StackNo, s.LotNo),
                    cartons,
                    quantity,
                    rate,
                    value);
            })
            .Where(l => !request.HideZeroQoh || l.Quantity != 0 || l.Cartons != 0)
            .OrderBy(l => l.LotNo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.StackNo, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new StackWiseStockReportDto(
            DateTime.UtcNow,
            asOfDate,
            lines.Count,
            lines.Sum(l => l.Quantity),
            lines.Sum(l => l.Cartons),
            lines.Sum(l => l.StockValue),
            lines);
    }

    public async Task<LowStockReportDto> GetLowStockReportAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var items = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.IsActive)
            .Select(i => new
            {
                i.Id,
                i.ItemCode,
                i.ItemName,
                CategoryName = i.ItemCategory != null ? i.ItemCategory.Name : null,
                UnitSymbol = i.UnitOfMeasure.Symbol ?? i.UnitOfMeasure.Name,
                i.CurrentStock,
                i.MinimumStock,
                i.ReorderLevel
            })
            .ToListAsync(cancellationToken);
        var lines = items
            .Where(i => i.CurrentStock < i.MinimumStock
                        || (i.ReorderLevel > 0 && i.CurrentStock <= i.ReorderLevel))
            .Select(i =>
            {
                var threshold = i.ReorderLevel > 0 ? i.ReorderLevel : i.MinimumStock;
                return new LowStockLineDto(
                    i.Id,
                    i.ItemCode,
                    i.ItemName,
                    i.CategoryName,
                    i.UnitSymbol,
                    i.CurrentStock,
                    i.MinimumStock,
                    i.ReorderLevel,
                    Math.Max(0m, threshold - i.CurrentStock));
            })
            .OrderBy(i => i.CurrentStock)
            .ThenBy(i => i.ItemCode)
            .ToList();
        return new LowStockReportDto(DateTime.UtcNow, lines.Count, lines);
    }

    public async Task<StockMovementReportDto> GetStockMovementReportAsync(
        StockMovementReportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.FromDate == default || request.ToDate == default)
        {
            throw new InvalidOperationException("From and to dates are required.");
        }
        if (request.FromDate.Date > request.ToDate.Date)
        {
            throw new InvalidOperationException("From date cannot be after to date.");
        }
        var companyId = _currentCompany.GetRequiredCompanyId();
        var from = request.FromDate.Date;
        var to = request.ToDate.Date.AddDays(1).AddTicks(-1);
        var query = _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId
                        && t.TransactionDate >= from
                        && t.TransactionDate <= to);
        if (request.ItemId.HasValue)
        {
            query = query.Where(t => t.ItemId == request.ItemId.Value);
        }
        if (request.WarehouseId.HasValue)
        {
            query = query.Where(t => t.WarehouseId == request.WarehouseId.Value);
        }
        string? itemLabel = null;
        if (request.ItemId.HasValue)
        {
            itemLabel = await _unitOfWork.Repository<Item>()
                .Query()
                .Where(i => i.Id == request.ItemId.Value && i.CompanyId == companyId)
                .Select(i => i.ItemCode + " — " + i.ItemName)
                .FirstOrDefaultAsync(cancellationToken);
        }
        string? warehouseLabel = null;
        if (request.WarehouseId.HasValue)
        {
            warehouseLabel = await _unitOfWork.Repository<Warehouse>()
                .Query()
                .Where(w => w.Id == request.WarehouseId.Value && w.CompanyId == companyId)
                .Select(w => w.Code + " — " + w.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }
        var transactions = await query
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.Id)
            .Select(t => new
            {
                t.ItemId,
                t.TransactionDate,
                t.ReferenceNo,
                t.TransactionType,
                t.Item.ItemCode,
                t.Item.ItemName,
                t.Warehouse.Name,
                t.Quantity,
                t.UnitCost,
                t.TotalCost,
                t.StackNo,
                t.LotNo,
                t.Notes
            })
            .ToListAsync(cancellationToken);
        var cartonResolver = await BuildMovementCartonResolverAsync(
            companyId,
            transactions
                .Where(t => !string.IsNullOrWhiteSpace(t.ReferenceNo))
                .Select(t => t.ReferenceNo!)
                .Distinct()
                .ToList(),
            cancellationToken);
        var movementItemIds = transactions.Select(t => t.ItemId).Distinct().ToList();
        if (request.ItemId.HasValue && !movementItemIds.Contains(request.ItemId.Value))
        {
            movementItemIds.Add(request.ItemId.Value);
        }
        var vendorRefs = await BuildVendorRefLookupsAsync(
            companyId,
            movementItemIds,
            to,
            cancellationToken);
        var lines = transactions
            .Select(t =>
            {
                var isIn = t.TransactionType is InventoryTransactionType.StockIn
                    or InventoryTransactionType.Opening;
                var isOut = t.TransactionType == InventoryTransactionType.StockOut;
                var movementQty = isIn || isOut ? t.Quantity : 0m;
                var cartons = cartonResolver.Resolve(
                    t.ReferenceNo,
                    t.ItemId,
                    t.StackNo,
                    t.LotNo,
                    movementQty);
                return new StockMovementLineDto(
                    t.TransactionDate,
                    t.ReferenceNo,
                    t.TransactionType.ToString(),
                    t.ItemCode,
                    t.ItemName,
                    t.Name,
                    isIn ? t.Quantity : 0m,
                    isOut ? t.Quantity : 0m,
                    isIn ? cartons : 0m,
                    isOut ? cartons : 0m,
                    t.TransactionType == InventoryTransactionType.Adjustment ? t.Quantity : 0m,
                    t.UnitCost,
                    t.TotalCost,
                    t.StackNo,
                    t.LotNo,
                    t.Notes,
                    vendorRefs.Resolve(t.ReferenceNo, t.ItemId, t.StackNo, t.LotNo));
            })
            .ToList();
        var missingOpeningLines = await BuildMissingOpeningStockMovementLinesAsync(
            companyId,
            from,
            to,
            request.ItemId,
            request.WarehouseId,
            warehouseLabel,
            vendorRefs,
            cancellationToken);
        if (missingOpeningLines.Count > 0)
        {
            lines = lines
                .Concat(missingOpeningLines)
                .ToList();
        }
        var coveredOpeningKeys = transactions
            .Where(t => t.TransactionType == InventoryTransactionType.Opening
                        && string.Equals(
                            t.ReferenceNo,
                            OpeningStockBillNumber,
                            StringComparison.OrdinalIgnoreCase))
            .Select(t => OpeningStackLotKey(t.ItemId, t.StackNo, t.LotNo))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (!string.Equals(line.ReferenceNo, OpeningStockBillNumber, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    line.TransactionType,
                    InventoryTransactionType.Opening.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var matchingTransaction = transactions.FirstOrDefault(t =>
                t.ItemCode == line.ItemCode
                && NormalizeMovementKeyPart(t.StackNo) == NormalizeMovementKeyPart(line.StackNo)
                && NormalizeMovementKeyPart(t.LotNo) == NormalizeMovementKeyPart(line.LotNo));
            if (matchingTransaction is null)
            {
                continue;
            }
            coveredOpeningKeys.Add(OpeningStackLotKey(
                matchingTransaction.ItemId,
                line.StackNo,
                line.LotNo));
        }
        var contextualOpeningLines = await BuildContextualOpeningStockLinesAsync(
            companyId,
            from,
            request.ItemId,
            request.WarehouseId,
            warehouseLabel,
            transactions.Select(t => (t.ItemId, t.StackNo, t.LotNo)),
            coveredOpeningKeys,
            vendorRefs,
            cancellationToken);
        if (contextualOpeningLines.Count > 0)
        {
            lines = lines
                .Concat(contextualOpeningLines)
                .ToList();
        }
        if (missingOpeningLines.Count > 0 || contextualOpeningLines.Count > 0)
        {
            lines = lines
                .OrderBy(l => l.TransactionDate)
                .ThenBy(l => l.ReferenceNo)
                .ThenBy(l => l.ItemCode)
                .ThenBy(l => l.StackNo)
                .ToList();
        }
        if (request.ItemId.HasValue)
        {
            lines = await ReconcileSoldOutItemCartonsAsync(
                companyId,
                request.ItemId.Value,
                lines,
                cancellationToken);
        }
        return new StockMovementReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            request.ItemId,
            itemLabel,
            request.WarehouseId,
            warehouseLabel,
            lines.Count,
            lines.Sum(l => l.QtyIn),
            lines.Sum(l => l.QtyOut),
            lines.Sum(l => l.CartonsIn),
            lines.Sum(l => l.CartonsOut),
            lines);
    }

    public async Task<StackMovementReportDto> GetStackMovementReportAsync(
        StackMovementReportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.FromDate == default || request.ToDate == default)
        {
            throw new InvalidOperationException("From and to dates are required.");
        }
        if (request.FromDate.Date > request.ToDate.Date)
        {
            throw new InvalidOperationException("From date cannot be after to date.");
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var from = request.FromDate.Date;
        var to = request.ToDate.Date.AddDays(1).AddTicks(-1);
        var stackFilter = string.IsNullOrWhiteSpace(request.StackNo) ? null : request.StackNo.Trim();

        string? itemLabel = null;
        if (request.ItemId.HasValue)
        {
            itemLabel = await _unitOfWork.Repository<Item>()
                .Query()
                .Where(i => i.Id == request.ItemId.Value && i.CompanyId == companyId)
                .Select(i => i.ItemCode + " — " + i.ItemName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? warehouseLabel = null;
        if (request.WarehouseId.HasValue)
        {
            warehouseLabel = await _unitOfWork.Repository<Warehouse>()
                .Query()
                .Where(w => w.Id == request.WarehouseId.Value && w.CompanyId == companyId)
                .Select(w => w.Code + " — " + w.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var periodQuery = BuildStackMovementTransactionQuery(
            companyId,
            request.ItemId,
            request.WarehouseId,
            stackFilter);
        var periodTransactions = await periodQuery
            .Where(t => t.TransactionDate >= from && t.TransactionDate <= to)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.Id)
            .Select(t => new StackMovementTxn(
                t.ItemId,
                t.TransactionDate,
                t.ReferenceNo,
                t.TransactionType,
                t.Item.ItemCode,
                t.Item.ItemName,
                t.Warehouse.Name,
                t.Quantity,
                t.UnitCost,
                t.TotalCost,
                t.StackNo,
                t.Item.StackNo,
                t.LotNo,
                t.Item.LotNo,
                t.Notes))
            .ToListAsync(cancellationToken);

        var keys = periodTransactions
            .Select(t => StackMovementKey.From(t.ItemId, t.StackNo, t.ItemStackNo))
            .ToHashSet();

        if (keys.Count == 0 && stackFilter is not null)
        {
            var openingOnlyKeys = await BuildStackMovementTransactionQuery(
                    companyId,
                    request.ItemId,
                    request.WarehouseId,
                    stackFilter)
                .Where(t => t.TransactionDate < from)
                .Select(t => new { t.ItemId, t.StackNo, ItemStackNo = t.Item.StackNo })
                .Distinct()
                .ToListAsync(cancellationToken);
            foreach (var row in openingOnlyKeys)
            {
                keys.Add(StackMovementKey.From(row.ItemId, row.StackNo, row.ItemStackNo));
            }
        }

        if (keys.Count == 0)
        {
            return new StackMovementReportDto(
                request.FromDate.Date,
                request.ToDate.Date,
                request.ItemId,
                itemLabel,
                stackFilter,
                request.WarehouseId,
                warehouseLabel,
                0,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                []);
        }

        var itemIds = keys.Select(k => k.ItemId).Distinct().ToList();
        var vendorRefs = await BuildVendorRefLookupsAsync(
            companyId,
            itemIds,
            to,
            cancellationToken);
        var openingRows = await BuildStackMovementTransactionQuery(
                companyId,
                request.ItemId,
                request.WarehouseId,
                stackFilter)
            .Where(t => t.TransactionDate < from && itemIds.Contains(t.ItemId))
            .Select(t => new
            {
                t.ItemId,
                t.StackNo,
                ItemStackNo = t.Item.StackNo,
                t.LotNo,
                ItemLotNo = t.Item.LotNo,
                t.Item.ItemCode,
                t.Item.ItemName,
                t.Warehouse.Name,
                SignedQty = t.TransactionType == InventoryTransactionType.StockOut
                    ? -t.Quantity
                    : t.Quantity
            })
            .ToListAsync(cancellationToken);

        var openingByKey = openingRows
            .GroupBy(t => StackMovementKey.From(t.ItemId, t.StackNo, t.ItemStackNo))
            .ToDictionary(
                g => g.Key,
                g => (
                    Qty: Math.Round(g.Sum(x => x.SignedQty), 2),
                    ItemCode: g.Select(x => x.ItemCode).First(),
                    ItemName: g.Select(x => x.ItemName).First(),
                    LotNo: FirstNonEmpty(g.Select(x => x.LotNo).Concat(g.Select(x => x.ItemLotNo))),
                    WarehouseName: g.Select(x => x.Name).First()));

        var cartonResolver = await BuildMovementCartonResolverAsync(
            companyId,
            periodTransactions
                .Where(t => !string.IsNullOrWhiteSpace(t.ReferenceNo))
                .Select(t => t.ReferenceNo!)
                .Distinct()
                .ToList(),
            cancellationToken);

        var openingCartonsByKey = await BuildStackCartonsAsOfAsync(
            companyId,
            itemIds,
            from.AddTicks(-1),
            stackFilter,
            cancellationToken);
        var periodPurchaseCartons = await BuildStackPurchaseCartonsInRangeAsync(
            companyId,
            itemIds,
            from,
            to,
            stackFilter,
            cancellationToken);
        var periodSalesCartons = await BuildStackSalesCartonsInRangeAsync(
            companyId,
            itemIds,
            from,
            to,
            stackFilter,
            cancellationToken);

        var movementsByKey = periodTransactions
            .GroupBy(t => StackMovementKey.From(t.ItemId, t.StackNo, t.ItemStackNo))
            .ToDictionary(
                g => g.Key,
                g => g.Select(t =>
                {
                    var stackNo = ResolveStackLotValue(t.StackNo, t.ItemStackNo);
                    var lotNo = ResolveStackLotValue(t.LotNo, t.ItemLotNo);
                    var isIn = t.TransactionType is InventoryTransactionType.StockIn
                        or InventoryTransactionType.Opening;
                    var isOut = t.TransactionType == InventoryTransactionType.StockOut;
                    var movementQty = isIn || isOut ? t.Quantity : 0m;
                    var cartons = cartonResolver.Resolve(
                        t.ReferenceNo,
                        t.ItemId,
                        stackNo,
                        lotNo,
                        movementQty);
                    return new StockMovementLineDto(
                        t.TransactionDate,
                        t.ReferenceNo,
                        t.TransactionType.ToString(),
                        t.ItemCode,
                        t.ItemName,
                        t.WarehouseName,
                        isIn ? t.Quantity : 0m,
                        isOut ? t.Quantity : 0m,
                        isIn ? cartons : 0m,
                        isOut ? cartons : 0m,
                        t.TransactionType == InventoryTransactionType.Adjustment ? t.Quantity : 0m,
                        t.UnitCost,
                        t.TotalCost,
                        stackNo,
                        lotNo,
                        t.Notes,
                        vendorRefs.Resolve(t.ReferenceNo, t.ItemId, stackNo, lotNo));
                }).ToList());

        var lines = new List<StackMovementLineDto>();
        foreach (var key in keys)
        {
            movementsByKey.TryGetValue(key, out var movements);
            movements ??= [];
            openingByKey.TryGetValue(key, out var opening);
            var cartonKey = StackCartonKey(key.ItemId, key.StackNo);
            var openingQty = opening.Qty;
            var openingCartons = openingCartonsByKey.GetValueOrDefault(cartonKey);
            var qtyIn = movements.Sum(m => m.QtyIn);
            var qtyOut = movements.Sum(m => m.QtyOut);
            var cartonsIn = periodPurchaseCartons.GetValueOrDefault(cartonKey);
            if (cartonsIn == 0m)
            {
                cartonsIn = movements.Sum(m => m.CartonsIn);
            }
            var cartonsOut = periodSalesCartons.GetValueOrDefault(cartonKey);
            if (cartonsOut == 0m)
            {
                cartonsOut = movements.Sum(m => m.CartonsOut);
            }
            var adjustmentQty = movements.Sum(m => m.AdjustmentQty);
            var closingQty = Math.Round(openingQty + qtyIn - qtyOut + adjustmentQty, 2);
            var closingCartons = Math.Round(openingCartons + cartonsIn - cartonsOut, 2);
            if (stackFilter is null
                && openingQty == 0m
                && qtyIn == 0m
                && qtyOut == 0m
                && adjustmentQty == 0m
                && openingCartons == 0m
                && cartonsIn == 0m
                && cartonsOut == 0m)
            {
                continue;
            }

            var sample = movements.FirstOrDefault();
            lines.Add(new StackMovementLineDto(
                key.ItemId,
                sample?.ItemCode ?? opening.ItemCode ?? string.Empty,
                sample?.ItemName ?? opening.ItemName ?? string.Empty,
                key.StackNo,
                sample?.LotNo ?? opening.LotNo,
                vendorRefs.ForStack(key.ItemId, key.StackNo, sample?.LotNo ?? opening.LotNo),
                sample?.WarehouseName ?? opening.WarehouseName ?? warehouseLabel,
                openingQty,
                openingCartons,
                qtyIn,
                qtyOut,
                cartonsIn,
                cartonsOut,
                adjustmentQty,
                closingQty,
                closingCartons,
                movements));
        }

        var missingItemIds = lines
            .Where(l => string.IsNullOrWhiteSpace(l.ItemCode))
            .Select(l => l.ItemId)
            .Distinct()
            .ToList();
        if (missingItemIds.Count > 0)
        {
            var itemLookup = await _unitOfWork.Repository<Item>()
                .Query()
                .Where(i => i.CompanyId == companyId && missingItemIds.Contains(i.Id))
                .Select(i => new { i.Id, i.ItemCode, i.ItemName, i.LotNo })
                .ToDictionaryAsync(i => i.Id, cancellationToken);
            lines = lines
                .Select(l =>
                {
                    if (!string.IsNullOrWhiteSpace(l.ItemCode) || !itemLookup.TryGetValue(l.ItemId, out var item))
                    {
                        return l;
                    }

                    return l with
                    {
                        ItemCode = item.ItemCode,
                        ItemName = item.ItemName,
                        LotNo = l.LotNo ?? (string.IsNullOrWhiteSpace(item.LotNo) ? null : item.LotNo)
                    };
                })
                .ToList();
        }

        lines = lines
            .OrderBy(l => l.StackNo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new StackMovementReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            request.ItemId,
            itemLabel,
            stackFilter,
            request.WarehouseId,
            warehouseLabel,
            lines.Count,
            lines.Sum(l => l.OpeningQty),
            lines.Sum(l => l.OpeningCartons),
            lines.Sum(l => l.QtyIn),
            lines.Sum(l => l.QtyOut),
            lines.Sum(l => l.CartonsIn),
            lines.Sum(l => l.CartonsOut),
            lines.Sum(l => l.ClosingQty),
            lines.Sum(l => l.ClosingCartons),
            lines);
    }

    public async Task<IReadOnlyList<InventoryReportItemLookupDto>> GetItemLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        return await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.IsActive)
            .OrderBy(i => i.ItemName)
            .Select(i => new InventoryReportItemLookupDto(i.Id, i.ItemCode, i.ItemName))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryReportWarehouseLookupDto>> GetWarehouseLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        return await _unitOfWork.Repository<Warehouse>()
            .Query()
            .Where(w => w.CompanyId == companyId && w.IsActive)
            .OrderBy(w => w.Name)
            .Select(w => new InventoryReportWarehouseLookupDto(w.Id, w.Code, w.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryReportCategoryLookupDto>> GetCategoryLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        return await _unitOfWork.Repository<ItemCategory>()
            .Query()
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.Name)
            .Select(c => new InventoryReportCategoryLookupDto(c.Id, c.Name))
            .ToListAsync(cancellationToken);
    }

    private static string FormatUnitSymbol(string symbol, string name)
    {
        if (string.Equals(symbol, "CTN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Carton", StringComparison.OrdinalIgnoreCase))
        {
            return "Ctn";
        }
        if (string.Equals(symbol, "KG", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Kilogram", StringComparison.OrdinalIgnoreCase))
        {
            return "kg";
        }
        return symbol;
    }

    /// <summary>
    /// Uses item purchase rate when set; otherwise values from inventory cost layers
    /// (unit cost on stock-in / vendor bill rates) so items like W361 with PurchaseRate=0 still show Rate/Value.
    /// </summary>
    private static (decimal Rate, decimal Value) ResolveStockValuation(
        InventoryCostingBatch? costingBatch,
        int itemId,
        string? stackNo,
        string? lotNo,
        string itemStackNo,
        string itemLotNo,
        decimal quantity,
        CostingMethod costingMethod,
        decimal purchaseRate)
    {
        var roundedQty = Math.Round(quantity, 2);
        if (purchaseRate > 0m || costingBatch is null || Math.Abs(roundedQty) <= 0.01m)
        {
            return (Math.Round(purchaseRate, 2), Math.Round(roundedQty * purchaseRate, 2));
        }

        var absQty = Math.Abs(roundedQty);
        var cost = costingBatch.Calculate(new InventoryLineCostRequest(
            itemId,
            stackNo,
            lotNo,
            itemStackNo ?? string.Empty,
            itemLotNo ?? string.Empty,
            absQty,
            costingMethod,
            purchaseRate));

        if (cost.UnitCost <= 0m)
        {
            return (0m, 0m);
        }

        var value = roundedQty < 0m
            ? -Math.Abs(cost.TotalCost)
            : cost.TotalCost;
        return (cost.UnitCost, Math.Round(value, 2));
    }

    private async Task<Dictionary<int, decimal>> BuildPostAsOfCartonDeltasAsync(
        int companyId,
        DateTime asOfEnd,
        IReadOnlyList<(int ItemId, string? LotNo)> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return new Dictionary<int, decimal>();
        }
        var itemIds = items.Select(i => i.ItemId).Distinct().ToList();
        var itemLots = items.ToDictionary(i => i.ItemId, i => NormalizeCartonLot(i.LotNo));
        var purchaseLines = await _unitOfWork.Repository<VendorBillLine>()
            .Query()
            .Where(l => l.ItemId != null
                        && itemIds.Contains(l.ItemId.Value)
                        && l.VendorBill.CompanyId == companyId
                        && l.VendorBill.BillDate > asOfEnd
                        && (l.VendorBill.Status == BillStatus.Approved
                            || l.VendorBill.BillNumber == OpeningStockBillNumber))
            .Select(l => new
            {
                ItemId = l.ItemId!.Value,
                l.LotNo,
                l.Cartons
            })
            .ToListAsync(cancellationToken);
        var salesLines = await _unitOfWork.Repository<SalesInvoiceLine>()
            .Query()
            .Where(l => itemIds.Contains(l.ItemId)
                        && l.SalesInvoice.CompanyId == companyId
                        && l.SalesInvoice.Status == InvoiceStatus.Posted
                        && l.SalesInvoice.InvoiceDate > asOfEnd)
            .Select(l => new
            {
                l.ItemId,
                l.LotNo,
                l.Cartons,
                l.SalesInvoice.InvoiceType
            })
            .ToListAsync(cancellationToken);
        var deltas = new Dictionary<int, decimal>();
        foreach (var item in items)
        {
            var itemLot = itemLots[item.ItemId];
            var futurePurchases = purchaseLines
                .Where(l => l.ItemId == item.ItemId && NormalizeCartonLot(l.LotNo) == itemLot)
                .Sum(l => Math.Round(l.Cartons, 2));
            var futureSales = salesLines
                .Where(l => l.ItemId == item.ItemId && NormalizeCartonLot(l.LotNo) == itemLot)
                .Sum(l => Math.Round(
                    l.InvoiceType == InvoiceType.CreditNote ? -l.Cartons : l.Cartons,
                    2));
            deltas[item.ItemId] = Math.Round(futurePurchases - futureSales, 2);
        }
        return deltas;
    }

    private static string NormalizeCartonLot(string? lotNo) =>
        (lotNo ?? string.Empty).Trim();

    private async Task<MovementCartonResolver> BuildMovementCartonResolverAsync(
        int companyId,
        IReadOnlyList<string> referenceNos,
        CancellationToken cancellationToken)
    {
        var resolver = new MovementCartonResolver();
        if (referenceNos.Count == 0)
        {
            return resolver;
        }
        var billLines = await _unitOfWork.Repository<VendorBillLine>()
            .Query()
            .Where(l => l.ItemId != null
                        && l.VendorBill.CompanyId == companyId
                        && l.VendorBill.Status == BillStatus.Approved
                        && referenceNos.Contains(l.VendorBill.BillNumber))
            .Select(l => new
            {
                l.VendorBill.BillNumber,
                ItemId = l.ItemId!.Value,
                l.StackNo,
                l.LotNo,
                l.Quantity,
                l.Cartons
            })
            .ToListAsync(cancellationToken);
        foreach (var line in billLines)
        {
            resolver.Add(
                line.BillNumber,
                line.ItemId,
                line.StackNo,
                line.LotNo,
                line.Cartons,
                line.Quantity);
        }
        var invoiceLines = await _unitOfWork.Repository<SalesInvoiceLine>()
            .Query()
            .Where(l => l.SalesInvoice.CompanyId == companyId
                        && l.SalesInvoice.Status == InvoiceStatus.Posted
                        && referenceNos.Contains(l.SalesInvoice.InvoiceNumber))
            .Select(l => new
            {
                l.SalesInvoice.InvoiceNumber,
                l.ItemId,
                l.StackNo,
                l.LotNo,
                l.Quantity,
                l.Cartons
            })
            .ToListAsync(cancellationToken);
        foreach (var line in invoiceLines)
        {
            resolver.Add(
                line.InvoiceNumber,
                line.ItemId,
                line.StackNo,
                line.LotNo,
                line.Cartons,
                line.Quantity);
        }
        return resolver;
    }

    private async Task<List<StockMovementLineDto>> ReconcileSoldOutItemCartonsAsync(
        int companyId,
        int itemId,
        IReadOnlyList<StockMovementLineDto> lines,
        CancellationToken cancellationToken)
    {
        var item = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.Id == itemId && i.CompanyId == companyId)
            .Select(i => new { i.CurrentStock, i.Cartons })
            .FirstOrDefaultAsync(cancellationToken);
        if (item is null)
        {
            return lines.ToList();
        }
        var cartonsOnHand = (await _itemCartonSyncService.GetCartonsOnHandByItemAsync(
            companyId,
            [itemId],
            cancellationToken)).GetValueOrDefault(itemId, item.Cartons);
        if (Math.Abs(item.CurrentStock) > 0.01m || Math.Abs(cartonsOnHand) > 0.01m)
        {
            return lines.ToList();
        }
        var totalQtyIn = lines.Sum(l => l.QtyIn);
        var totalQtyOut = lines.Sum(l => l.QtyOut);
        if (Math.Abs(totalQtyIn - totalQtyOut) > 0.01m)
        {
            return lines.ToList();
        }
        var totalCtnIn = lines.Sum(l => l.CartonsIn);
        var totalCtnOut = lines.Sum(l => l.CartonsOut);
        var gap = Math.Round(totalCtnIn - totalCtnOut, 2);
        if (Math.Abs(gap) < 0.01m)
        {
            return lines.ToList();
        }
        var outLines = lines
            .Select((line, index) => (line, index))
            .Where(x => x.line.QtyOut > 0m)
            .ToList();
        if (outLines.Count == 0)
        {
            return lines.ToList();
        }
        var updated = lines.ToList();
        var totalQtyOutForAllocation = outLines.Sum(x => x.line.QtyOut);
        var remaining = gap;
        for (var i = 0; i < outLines.Count; i++)
        {
            var (line, index) = outLines[i];
            var add = i == outLines.Count - 1
                ? remaining
                : Math.Round(gap * line.QtyOut / totalQtyOutForAllocation, 2);
            remaining = Math.Round(remaining - add, 2);
            updated[index] = line with
            {
                CartonsOut = Math.Round(line.CartonsOut + add, 2)
            };
        }
        return updated;
    }

    private static string NormalizeMovementKeyPart(string? value) =>
        MovementCartonResolver.NormalizeKeyPart(value);

    private async Task<IReadOnlyList<StockMovementLineDto>> BuildMissingOpeningStockMovementLinesAsync(
        int companyId,
        DateTime from,
        DateTime to,
        int? itemId,
        int? warehouseId,
        string? warehouseLabel,
        VendorRefLookups vendorRefs,
        CancellationToken cancellationToken)
    {
        int? defaultWarehouseId = warehouseId;
        if (!defaultWarehouseId.HasValue)
        {
            defaultWarehouseId = await _unitOfWork.Repository<Warehouse>()
                .Query()
                .Where(w => w.CompanyId == companyId && w.IsActive)
                .OrderBy(w => w.Code)
                .Select(w => (int?)w.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (!defaultWarehouseId.HasValue)
        {
            return [];
        }
        if (OpeningStockBillDate < from || OpeningStockBillDate > to)
        {
            return [];
        }
        var openingLinesQuery = _unitOfWork.Repository<VendorBillLine>()
            .Query()
            .Where(l => l.ItemId != null
                        && l.VendorBill.CompanyId == companyId
                        && (l.VendorBill.BillNumber == OpeningStockBillNumber
                            || l.VendorBill.RefNo == OpeningStockRefNo));
        if (itemId.HasValue)
        {
            openingLinesQuery = openingLinesQuery.Where(l => l.ItemId == itemId.Value);
        }
        var openingLines = await openingLinesQuery
            .Select(l => new
            {
                ItemId = l.ItemId!.Value,
                l.VendorBill.BillNumber,
                l.VendorBill.BillDate,
                ItemCode = l.Item!.ItemCode,
                ItemName = l.Item.ItemName,
                l.Quantity,
                l.Cartons,
                l.Rate,
                l.Amount,
                l.StackNo,
                l.LotNo,
                l.VendorBill.RefNo
            })
            .ToListAsync(cancellationToken);
        if (openingLines.Count == 0)
        {
            return [];
        }
        var existingKeys = await _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId
                        && (t.ReferenceNo == OpeningStockBillNumber
                            || (t.Notes != null && t.Notes.Contains(OpeningStockBillNumber))))
            .Select(t => new
            {
                t.ItemId,
                t.Quantity,
                StackNo = t.StackNo ?? string.Empty,
                LotNo = t.LotNo ?? string.Empty
            })
            .ToListAsync(cancellationToken);
        var existingSet = existingKeys
            .Select(k => OpeningBillLineKey(k.ItemId, k.Quantity, k.StackNo, k.LotNo))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var warehouseName = !string.IsNullOrWhiteSpace(warehouseLabel)
            ? warehouseLabel.Split(" — ", 2).Last()
            : await _unitOfWork.Repository<Warehouse>()
                .Query()
                .Where(w => w.Id == defaultWarehouseId.Value)
                .Select(w => w.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "Default";
        return openingLines
            .Where(l => l.Quantity > 0m)
            .Where(l => !existingSet.Contains(OpeningBillLineKey(l.ItemId, l.Quantity, l.StackNo, l.LotNo)))
            .Select(l => new StockMovementLineDto(
                l.BillDate,
                l.BillNumber,
                InventoryTransactionType.Opening.ToString(),
                l.ItemCode,
                l.ItemName,
                warehouseName,
                l.Quantity,
                0m,
                Math.Round(l.Cartons, 2),
                0m,
                0m,
                l.Rate,
                l.Amount,
                l.StackNo,
                l.LotNo,
                $"Opening stock {l.BillNumber}",
                UsefulVendorRef(l.RefNo)
                    ?? vendorRefs.Resolve(l.BillNumber, l.ItemId, l.StackNo, l.LotNo)))
            .ToList();
    }

    private static string OpeningStackLotKey(int itemId, string? stackNo, string? lotNo) =>
        $"{itemId}|{NormalizeMovementKeyPart(stackNo)}|{NormalizeMovementKeyPart(lotNo)}";

    private static string OpeningBillLineKey(
        int itemId,
        decimal quantity,
        string? stackNo,
        string? lotNo) =>
        $"{itemId}|{quantity:0.00}|{NormalizeMovementKeyPart(stackNo)}|{NormalizeMovementKeyPart(lotNo)}";

    private async Task<IReadOnlyList<StockMovementLineDto>> BuildContextualOpeningStockLinesAsync(
        int companyId,
        DateTime from,
        int? itemId,
        int? warehouseId,
        string? warehouseLabel,
        IEnumerable<(int ItemId, string? StackNo, string? LotNo)> periodActivity,
        IReadOnlySet<string> coveredOpeningStackLotKeys,
        VendorRefLookups vendorRefs,
        CancellationToken cancellationToken)
    {
        if (from <= OpeningStockBillDate.Date)
        {
            return [];
        }
        var activityKeys = periodActivity
            .Select(a => OpeningStackLotKey(a.ItemId, a.StackNo, a.LotNo))
            .Where(key => !coveredOpeningStackLotKeys.Contains(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (activityKeys.Count == 0)
        {
            return [];
        }
        var query = _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId
                        && t.TransactionType == InventoryTransactionType.Opening
                        && (t.ReferenceNo == OpeningStockBillNumber
                            || (t.Notes != null && t.Notes.Contains(OpeningStockBillNumber)))
                        && t.TransactionDate < from);
        if (itemId.HasValue)
        {
            query = query.Where(t => t.ItemId == itemId.Value);
        }
        if (warehouseId.HasValue)
        {
            query = query.Where(t => t.WarehouseId == warehouseId.Value);
        }
        var openingTransactions = await query
            .Select(t => new
            {
                t.ItemId,
                t.TransactionDate,
                t.ReferenceNo,
                t.Item.ItemCode,
                t.Item.ItemName,
                t.Warehouse.Name,
                t.Quantity,
                t.UnitCost,
                t.TotalCost,
                t.StackNo,
                t.LotNo,
                t.Notes
            })
            .ToListAsync(cancellationToken);
        var matchingTransactions = openingTransactions
            .Where(t => activityKeys.Contains(OpeningStackLotKey(t.ItemId, t.StackNo, t.LotNo)))
            .ToList();
        if (matchingTransactions.Count == 0)
        {
            return [];
        }
        var cartonResolver = await BuildMovementCartonResolverAsync(
            companyId,
            [OpeningStockBillNumber],
            cancellationToken);
        return matchingTransactions
            .Select(t =>
            {
                var cartons = cartonResolver.Resolve(
                    t.ReferenceNo,
                    t.ItemId,
                    t.StackNo,
                    t.LotNo,
                    t.Quantity);
                return new StockMovementLineDto(
                    t.TransactionDate,
                    t.ReferenceNo,
                    InventoryTransactionType.Opening.ToString(),
                    t.ItemCode,
                    t.ItemName,
                    t.Name,
                    t.Quantity,
                    0m,
                    cartons,
                    0m,
                    0m,
                    t.UnitCost,
                    t.TotalCost,
                    t.StackNo,
                    t.LotNo,
                    t.Notes,
                    vendorRefs.Resolve(t.ReferenceNo, t.ItemId, t.StackNo, t.LotNo));
            })
            .ToList();
    }

    private async Task<Dictionary<string, decimal>> BuildStackPurchaseCartonsAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        DateTime asOfEnd,
        CancellationToken cancellationToken)
    {
        var lines = await _unitOfWork.Repository<VendorBillLine>()
            .Query()
            .Where(l => l.ItemId.HasValue
                        && itemIds.Contains(l.ItemId.Value)
                        && l.VendorBill.CompanyId == companyId
                        && l.VendorBill.BillDate <= asOfEnd
                        && (l.VendorBill.Status == BillStatus.Approved
                            || l.VendorBill.BillNumber == OpeningStockBillNumber))
            .Select(l => new
            {
                ItemId = l.ItemId!.Value,
                StackNo = l.StackNo ?? string.Empty,
                l.Cartons
            })
            .ToListAsync(cancellationToken);
        return lines
            .GroupBy(l => StackCartonKey(l.ItemId, l.StackNo))
            .ToDictionary(g => g.Key, g => Math.Round(g.Sum(x => x.Cartons), 2));
    }

    private async Task<Dictionary<string, decimal>> BuildStackSalesCartonsAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        DateTime asOfEnd,
        CancellationToken cancellationToken)
    {
        var lines = await _unitOfWork.Repository<SalesInvoiceLine>()
            .Query()
            .Where(l => itemIds.Contains(l.ItemId)
                        && l.SalesInvoice.CompanyId == companyId
                        && l.SalesInvoice.Status == InvoiceStatus.Posted
                        && l.SalesInvoice.InvoiceDate <= asOfEnd)
            .Select(l => new
            {
                l.ItemId,
                StackNo = l.StackNo ?? string.Empty,
                l.Cartons,
                l.SalesInvoice.InvoiceType
            })
            .ToListAsync(cancellationToken);
        return lines
            .GroupBy(l => StackCartonKey(l.ItemId, l.StackNo))
            .ToDictionary(
                g => g.Key,
                g => Math.Round(g.Sum(x =>
                    x.InvoiceType == InvoiceType.CreditNote ? -x.Cartons : x.Cartons), 2));
    }

    private async Task<Dictionary<string, decimal>> BuildLotPurchaseCartonsAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        DateTime asOfEnd,
        CancellationToken cancellationToken)
    {
        var lines = await _unitOfWork.Repository<VendorBillLine>()
            .Query()
            .Where(l => l.ItemId.HasValue
                        && itemIds.Contains(l.ItemId.Value)
                        && l.VendorBill.CompanyId == companyId
                        && l.VendorBill.BillDate <= asOfEnd
                        && (l.VendorBill.Status == BillStatus.Approved
                            || l.VendorBill.BillNumber == OpeningStockBillNumber))
            .Select(l => new
            {
                ItemId = l.ItemId!.Value,
                LotNo = l.LotNo ?? l.Item!.LotNo ?? string.Empty,
                l.Cartons
            })
            .ToListAsync(cancellationToken);
        return lines
            .GroupBy(l => LotCartonKey(l.ItemId, l.LotNo))
            .ToDictionary(g => g.Key, g => Math.Round(g.Sum(x => x.Cartons), 2));
    }

    private async Task<Dictionary<string, decimal>> BuildLotSalesCartonsAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        DateTime asOfEnd,
        CancellationToken cancellationToken)
    {
        var lines = await _unitOfWork.Repository<SalesInvoiceLine>()
            .Query()
            .Where(l => itemIds.Contains(l.ItemId)
                        && l.SalesInvoice.CompanyId == companyId
                        && l.SalesInvoice.Status == InvoiceStatus.Posted
                        && l.SalesInvoice.InvoiceDate <= asOfEnd)
            .Select(l => new
            {
                l.ItemId,
                LotNo = l.LotNo ?? l.Item.LotNo ?? string.Empty,
                l.Cartons,
                l.SalesInvoice.InvoiceType
            })
            .ToListAsync(cancellationToken);
        return lines
            .GroupBy(l => LotCartonKey(l.ItemId, l.LotNo))
            .ToDictionary(
                g => g.Key,
                g => Math.Round(g.Sum(x =>
                    x.InvoiceType == InvoiceType.CreditNote ? -x.Cartons : x.Cartons), 2));
    }

    private IQueryable<InventoryTransaction> BuildStackMovementTransactionQuery(
        int companyId,
        int? itemId,
        int? warehouseId,
        string? stackNo)
    {
        var query = _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId && !t.IsDeleted);
        if (itemId.HasValue)
        {
            query = query.Where(t => t.ItemId == itemId.Value);
        }
        if (warehouseId.HasValue)
        {
            query = query.Where(t => t.WarehouseId == warehouseId.Value);
        }
        if (!string.IsNullOrWhiteSpace(stackNo))
        {
            query = query.Where(t =>
                t.StackNo == stackNo
                || ((t.StackNo == null || t.StackNo == "") && t.Item.StackNo == stackNo));
        }

        return query;
    }

    private async Task<Dictionary<string, decimal>> BuildStackCartonsAsOfAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        DateTime asOfEnd,
        string? stackNo,
        CancellationToken cancellationToken)
    {
        var purchases = await BuildStackPurchaseCartonsInRangeAsync(
            companyId,
            itemIds,
            null,
            asOfEnd,
            stackNo,
            cancellationToken);
        var sales = await BuildStackSalesCartonsInRangeAsync(
            companyId,
            itemIds,
            null,
            asOfEnd,
            stackNo,
            cancellationToken);
        var keys = purchases.Keys.Concat(sales.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            result[key] = Math.Round(
                purchases.GetValueOrDefault(key) - sales.GetValueOrDefault(key),
                2);
        }

        return result;
    }

    private async Task<Dictionary<string, decimal>> BuildStackPurchaseCartonsInRangeAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        DateTime? fromInclusive,
        DateTime toInclusive,
        string? stackNo,
        CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        var query = _unitOfWork.Repository<VendorBillLine>()
            .Query()
            .Where(l => l.ItemId.HasValue
                        && itemIds.Contains(l.ItemId.Value)
                        && l.VendorBill.CompanyId == companyId
                        && l.VendorBill.BillDate <= toInclusive
                        && (l.VendorBill.Status == BillStatus.Approved
                            || l.VendorBill.BillNumber == OpeningStockBillNumber));
        if (fromInclusive.HasValue)
        {
            query = query.Where(l => l.VendorBill.BillDate >= fromInclusive.Value);
        }
        if (!string.IsNullOrWhiteSpace(stackNo))
        {
            query = query.Where(l =>
                l.StackNo == stackNo
                || ((l.StackNo == null || l.StackNo == "") && l.Item!.StackNo == stackNo));
        }

        var lines = await query
            .Select(l => new
            {
                ItemId = l.ItemId!.Value,
                l.StackNo,
                ItemStackNo = l.Item!.StackNo,
                l.Cartons
            })
            .ToListAsync(cancellationToken);
        return lines
            .GroupBy(l => StackCartonKey(l.ItemId, ResolveStackLotValue(l.StackNo, l.ItemStackNo)))
            .ToDictionary(
                g => g.Key,
                g => Math.Round(g.Sum(x => x.Cartons), 2),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, decimal>> BuildStackSalesCartonsInRangeAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        DateTime? fromInclusive,
        DateTime toInclusive,
        string? stackNo,
        CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        var query = _unitOfWork.Repository<SalesInvoiceLine>()
            .Query()
            .Where(l => itemIds.Contains(l.ItemId)
                        && l.SalesInvoice.CompanyId == companyId
                        && l.SalesInvoice.Status == InvoiceStatus.Posted
                        && l.SalesInvoice.InvoiceDate <= toInclusive);
        if (fromInclusive.HasValue)
        {
            query = query.Where(l => l.SalesInvoice.InvoiceDate >= fromInclusive.Value);
        }
        if (!string.IsNullOrWhiteSpace(stackNo))
        {
            query = query.Where(l =>
                l.StackNo == stackNo
                || ((l.StackNo == null || l.StackNo == "") && l.Item.StackNo == stackNo));
        }

        var lines = await query
            .Select(l => new
            {
                l.ItemId,
                l.StackNo,
                ItemStackNo = l.Item.StackNo,
                l.Cartons,
                l.SalesInvoice.InvoiceType
            })
            .ToListAsync(cancellationToken);
        return lines
            .GroupBy(l => StackCartonKey(l.ItemId, ResolveStackLotValue(l.StackNo, l.ItemStackNo)))
            .ToDictionary(
                g => g.Key,
                g => Math.Round(g.Sum(x =>
                    x.InvoiceType == InvoiceType.CreditNote ? -x.Cartons : x.Cartons), 2),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<VendorRefLookups> BuildVendorRefLookupsAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        DateTime? asOfEnd,
        CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return VendorRefLookups.Empty;
        }

        var query = _unitOfWork.Repository<VendorBillLine>()
            .Query()
            .Where(l => l.ItemId.HasValue
                        && itemIds.Contains(l.ItemId.Value)
                        && l.VendorBill.CompanyId == companyId
                        && (l.VendorBill.Status == BillStatus.Approved
                            || l.VendorBill.BillNumber == OpeningStockBillNumber));
        if (asOfEnd.HasValue)
        {
            query = query.Where(l => l.VendorBill.BillDate <= asOfEnd.Value);
        }

        var rows = await query
            .Select(l => new
            {
                ItemId = l.ItemId!.Value,
                l.StackNo,
                ItemStackNo = l.Item!.StackNo,
                l.LotNo,
                ItemLotNo = l.Item.LotNo,
                l.VendorBill.BillNumber,
                l.VendorBill.RefNo,
                l.VendorBill.BillDate
            })
            .ToListAsync(cancellationToken);

        var byBill = rows
            .GroupBy(r => (r.BillNumber ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, Ref: JoinVendorRefs(g.Select(x => (x.BillDate, x.RefNo)))))
            .Where(x => x.Key.Length > 0 && x.Ref is not null)
            .ToDictionary(x => x.Key, x => x.Ref!, StringComparer.OrdinalIgnoreCase);

        var byStack = rows
            .GroupBy(r => StackCartonKey(r.ItemId, ResolveStackLotValue(r.StackNo, r.ItemStackNo)))
            .Select(g => (g.Key, Ref: JoinVendorRefs(g.Select(x => (x.BillDate, x.RefNo)))))
            .Where(x => x.Ref is not null)
            .ToDictionary(x => x.Key, x => x.Ref!, StringComparer.OrdinalIgnoreCase);

        var byLot = rows
            .GroupBy(r => LotCartonKey(r.ItemId, ResolveStackLotValue(r.LotNo, r.ItemLotNo)))
            .Select(g => (g.Key, Ref: JoinVendorRefs(g.Select(x => (x.BillDate, x.RefNo)))))
            .Where(x => x.Ref is not null)
            .ToDictionary(x => x.Key, x => x.Ref!, StringComparer.OrdinalIgnoreCase);

        return new VendorRefLookups(byBill, byStack, byLot);
    }

    private static string? UsefulVendorRef(string? refNo)
    {
        if (string.IsNullOrWhiteSpace(refNo))
        {
            return null;
        }

        var trimmed = refNo.Trim();
        if (string.Equals(trimmed, OpeningStockRefNo, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, OpeningStockBillNumber, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    private static string? JoinVendorRefs(IEnumerable<(DateTime Date, string? RefNo)> rows)
    {
        var refs = rows
            .Select(r => UsefulVendorRef(r.RefNo))
            .Where(r => r is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return refs.Count == 0 ? null : string.Join(", ", refs);
    }

    private static string? ResolveStackLotValue(string? lineValue, string? itemValue)
    {
        if (!string.IsNullOrWhiteSpace(lineValue))
        {
            return lineValue.Trim();
        }

        return string.IsNullOrWhiteSpace(itemValue) ? null : itemValue.Trim();
    }

    private static string? FirstNonEmpty(IEnumerable<string?> values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string StackCartonKey(int itemId, string? stackNo) =>
        $"{itemId}|{(stackNo ?? string.Empty).Trim()}";

    private static string LotCartonKey(int itemId, string? lotNo) =>
        $"{itemId}|{(lotNo ?? string.Empty).Trim().ToUpperInvariant()}";

    private sealed class VendorRefLookups
    {
        public static VendorRefLookups Empty { get; } = new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        private readonly Dictionary<string, string> _byBillNumber;
        private readonly Dictionary<string, string> _byStack;
        private readonly Dictionary<string, string> _byLot;

        public VendorRefLookups(
            Dictionary<string, string> byBillNumber,
            Dictionary<string, string> byStack,
            Dictionary<string, string> byLot)
        {
            _byBillNumber = byBillNumber;
            _byStack = byStack;
            _byLot = byLot;
        }

        public string? Resolve(string? billNumber, int itemId, string? stackNo, string? lotNo)
        {
            if (!string.IsNullOrWhiteSpace(billNumber)
                && _byBillNumber.TryGetValue(billNumber.Trim(), out var byBill)
                && !string.IsNullOrWhiteSpace(byBill))
            {
                return byBill;
            }

            return ForStack(itemId, stackNo, lotNo);
        }

        public string? ForStack(int itemId, string? stackNo, string? lotNo)
        {
            if (_byStack.TryGetValue(StackCartonKey(itemId, stackNo), out var byStack)
                && !string.IsNullOrWhiteSpace(byStack))
            {
                return byStack;
            }

            if (string.IsNullOrWhiteSpace(stackNo))
            {
                return ForLot(itemId, lotNo);
            }

            return null;
        }

        public string? ForLot(int itemId, string? lotNo)
        {
            if (_byLot.TryGetValue(LotCartonKey(itemId, lotNo), out var byLot)
                && !string.IsNullOrWhiteSpace(byLot))
            {
                return byLot;
            }

            return null;
        }
    }

    private sealed record StackMovementKey(int ItemId, string? StackNo)
    {
        public static StackMovementKey From(int itemId, string? stackNo, string? itemStackNo) =>
            new(itemId, ResolveStackLotValue(stackNo, itemStackNo));
    }

    private sealed record StackMovementTxn(
        int ItemId,
        DateTime TransactionDate,
        string? ReferenceNo,
        InventoryTransactionType TransactionType,
        string ItemCode,
        string ItemName,
        string WarehouseName,
        decimal Quantity,
        decimal UnitCost,
        decimal TotalCost,
        string? StackNo,
        string? ItemStackNo,
        string? LotNo,
        string? ItemLotNo,
        string? Notes);
}
