using Microsoft.EntityFrameworkCore;

using PakistanAccountingERP.Application.DTOs;

using PakistanAccountingERP.Application.Interfaces;

using PakistanAccountingERP.Application.Interfaces.Services;

using PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Enums;



namespace PakistanAccountingERP.Application.Services;



public class InventoryReportService : IInventoryReportService

{

    private const string OpeningStockRefNo = "OPENING-31MAY2026";

    private const string OpeningStockBillNumber = "OPEN-STOCK-31052026";

    private static readonly DateTime OpeningStockBillDate = new(2026, 5, 31);

    private readonly IUnitOfWork _unitOfWork;

    private readonly ICurrentCompanyService _currentCompany;



    public InventoryReportService(IUnitOfWork unitOfWork, ICurrentCompanyService currentCompany)

    {

        _unitOfWork = unitOfWork;

        _currentCompany = currentCompany;

    }



    public async Task<StockSummaryReportDto> GetStockSummaryAsync(

        StockSummaryReportRequest request,

        CancellationToken cancellationToken = default)

    {

        var companyId = _currentCompany.GetRequiredCompanyId();

        var asOfDate = (request.AsOfDate ?? DateTime.UtcNow).Date;

        var asOfEnd = asOfDate.AddDays(1).AddTicks(-1);

        var today = DateTime.UtcNow.Date;



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

                i.MinimumStock,

                i.ReorderLevel,

                i.PurchaseRate

            })

            .ToListAsync(cancellationToken);



        Dictionary<int, decimal>? postAsOfDeltas = null;

        Dictionary<int, decimal>? postAsOfCartonDeltas = null;

        if (asOfDate < today)

        {

            postAsOfDeltas = await _unitOfWork.Repository<InventoryTransaction>()

                .Query()

                .Where(t => t.CompanyId == companyId && t.TransactionDate > asOfEnd)

                .GroupBy(t => t.ItemId)

                .Select(g => new

                {

                    ItemId = g.Key,

                    Delta = g.Sum(t =>

                        t.TransactionType == InventoryTransactionType.StockOut

                            ? -t.Quantity

                            : t.Quantity)

                })

                .ToDictionaryAsync(x => x.ItemId, x => x.Delta, cancellationToken);

            postAsOfCartonDeltas = await BuildPostAsOfCartonDeltasAsync(

                companyId,

                asOfEnd,

                cancellationToken);

        }

        var cartonBalances = await BuildItemCartonBalancesAsync(companyId, cancellationToken);



        var lines = items

            .Select(i =>

            {

                var stockOnHand = i.CurrentStock;

                if (postAsOfDeltas != null && postAsOfDeltas.TryGetValue(i.Id, out var delta))

                {

                    stockOnHand = i.CurrentStock - delta;

                }

                var cartonsOnHand = cartonBalances.GetValueOrDefault(i.Id);

                if (postAsOfCartonDeltas != null && postAsOfCartonDeltas.TryGetValue(i.Id, out var cartonDelta))

                {

                    cartonsOnHand = Math.Round(cartonsOnHand - cartonDelta, 2);

                }



                return new StockSummaryLineDto(

                    i.Id,

                    i.ItemCode,

                    i.ItemName,

                    i.CategoryName,

                    FormatUnitSymbol(i.UnitSymbol, i.UnitName),

                    stockOnHand,

                    cartonsOnHand,

                    i.MinimumStock,

                    i.ReorderLevel,

                    i.PurchaseRate,

                    stockOnHand * i.PurchaseRate);

            })

            .Where(l => !request.HideZeroQoh || l.CurrentStock != 0 || l.CurrentCartons != 0)

            .ToList();



        return new StockSummaryReportDto(

            DateTime.UtcNow,

            asOfDate,

            lines.Count,

            lines.Sum(l => l.StockValue),

            lines.Sum(l => l.CurrentCartons),

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



        var cartonLookup = await BuildMovementCartonLookupAsync(

            companyId,

            transactions

                .Where(t => !string.IsNullOrWhiteSpace(t.ReferenceNo))

                .Select(t => t.ReferenceNo!)

                .Distinct()

                .ToList(),

            cancellationToken);



        var lines = transactions

            .Select(t =>

            {

                var isIn = t.TransactionType is InventoryTransactionType.StockIn

                    or InventoryTransactionType.Opening;

                var isOut = t.TransactionType == InventoryTransactionType.StockOut;

                var cartons = ResolveMovementCartons(

                    cartonLookup,

                    t.ReferenceNo,

                    t.ItemId,

                    t.StackNo,

                    t.LotNo);

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

                    t.Notes);

            })

            .ToList();



        var missingOpeningLines = await BuildMissingOpeningStockMovementLinesAsync(

            companyId,

            from,

            to,

            request.ItemId,

            request.WarehouseId,

            warehouseLabel,

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



    private async Task<Dictionary<int, decimal>> BuildItemCartonBalancesAsync(

        int companyId,

        CancellationToken cancellationToken)

    {

        var purchaseCartons = await _unitOfWork.Repository<VendorBillLine>()

            .Query()

            .Where(l => l.ItemId != null

                        && l.VendorBill.CompanyId == companyId

                        && l.VendorBill.Status == BillStatus.Approved)

            .GroupBy(l => l.ItemId!.Value)

            .Select(g => new { ItemId = g.Key, Total = g.Sum(l => l.Cartons) })

            .ToDictionaryAsync(x => x.ItemId, x => x.Total, cancellationToken);



        var salesCartons = await _unitOfWork.Repository<SalesInvoiceLine>()

            .Query()

            .Where(l => l.SalesInvoice.CompanyId == companyId

                        && l.SalesInvoice.Status == InvoiceStatus.Posted)

            .GroupBy(l => l.ItemId)

            .Select(g => new

            {

                ItemId = g.Key,

                Total = g.Sum(l =>

                    l.SalesInvoice.InvoiceType == InvoiceType.CreditNote ? -l.Cartons : l.Cartons)

            })

            .ToDictionaryAsync(x => x.ItemId, x => x.Total, cancellationToken);



        var itemIds = purchaseCartons.Keys.Union(salesCartons.Keys);

        return itemIds.ToDictionary(

            id => id,

            id => Math.Round(

                purchaseCartons.GetValueOrDefault(id) - salesCartons.GetValueOrDefault(id),

                2));

    }



    private async Task<Dictionary<int, decimal>> BuildPostAsOfCartonDeltasAsync(

        int companyId,

        DateTime asOfEnd,

        CancellationToken cancellationToken)

    {

        var futurePurchaseCartons = await _unitOfWork.Repository<VendorBillLine>()

            .Query()

            .Where(l => l.ItemId != null

                        && l.VendorBill.CompanyId == companyId

                        && l.VendorBill.Status == BillStatus.Approved

                        && l.VendorBill.BillDate > asOfEnd)

            .GroupBy(l => l.ItemId!.Value)

            .Select(g => new { ItemId = g.Key, Total = g.Sum(l => l.Cartons) })

            .ToDictionaryAsync(x => x.ItemId, x => x.Total, cancellationToken);



        var futureSalesCartons = await _unitOfWork.Repository<SalesInvoiceLine>()

            .Query()

            .Where(l => l.SalesInvoice.CompanyId == companyId

                        && l.SalesInvoice.Status == InvoiceStatus.Posted

                        && l.SalesInvoice.InvoiceDate > asOfEnd)

            .GroupBy(l => l.ItemId)

            .Select(g => new

            {

                ItemId = g.Key,

                Total = g.Sum(l =>

                    l.SalesInvoice.InvoiceType == InvoiceType.CreditNote ? -l.Cartons : l.Cartons)

            })

            .ToDictionaryAsync(x => x.ItemId, x => x.Total, cancellationToken);



        var itemIds = futurePurchaseCartons.Keys.Union(futureSalesCartons.Keys);

        return itemIds.ToDictionary(

            id => id,

            id => Math.Round(

                futurePurchaseCartons.GetValueOrDefault(id) - futureSalesCartons.GetValueOrDefault(id),

                2));

    }



    private async Task<Dictionary<string, decimal>> BuildMovementCartonLookupAsync(

        int companyId,

        IReadOnlyList<string> referenceNos,

        CancellationToken cancellationToken)

    {

        if (referenceNos.Count == 0)

        {

            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

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

                l.Cartons

            })

            .ToListAsync(cancellationToken);



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

                l.Cartons

            })

            .ToListAsync(cancellationToken);



        var lookup = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in billLines)

        {

            lookup[MovementCartonKey(line.BillNumber, line.ItemId, line.StackNo, line.LotNo)] =

                Math.Round(line.Cartons, 2);

        }



        foreach (var line in invoiceLines)

        {

            lookup[MovementCartonKey(line.InvoiceNumber, line.ItemId, line.StackNo, line.LotNo)] =

                Math.Round(line.Cartons, 2);

        }



        return lookup;

    }



    private static decimal ResolveMovementCartons(

        IReadOnlyDictionary<string, decimal> lookup,

        string? referenceNo,

        int itemId,

        string? stackNo,

        string? lotNo)

    {

        if (string.IsNullOrWhiteSpace(referenceNo))

        {

            return 0m;

        }



        return lookup.TryGetValue(

            MovementCartonKey(referenceNo, itemId, stackNo, lotNo),

            out var cartons)

            ? cartons

            : 0m;

    }



    private static string MovementCartonKey(

        string referenceNo,

        int itemId,

        string? stackNo,

        string? lotNo) =>

        $"{referenceNo.Trim().ToUpperInvariant()}|{itemId}|{NormalizeMovementKeyPart(stackNo)}|{NormalizeMovementKeyPart(lotNo)}";



    private static string NormalizeMovementKeyPart(string? value) =>

        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();



    private async Task<IReadOnlyList<StockMovementLineDto>> BuildMissingOpeningStockMovementLinesAsync(

        int companyId,

        DateTime from,

        DateTime to,

        int? itemId,

        int? warehouseId,

        string? warehouseLabel,

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

                        && l.VendorBill.Status == BillStatus.Approved

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

                l.LotNo

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

                $"Opening stock {l.BillNumber}"))

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



        var cartonLookup = await BuildMovementCartonLookupAsync(

            companyId,

            [OpeningStockBillNumber],

            cancellationToken);



        return matchingTransactions

            .Select(t =>

            {

                var cartons = ResolveMovementCartons(

                    cartonLookup,

                    t.ReferenceNo,

                    t.ItemId,

                    t.StackNo,

                    t.LotNo);

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

                    t.Notes);

            })

            .ToList();

    }

}


