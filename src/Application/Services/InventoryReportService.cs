using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class InventoryReportService : IInventoryReportService
{
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
        }

        var lines = items
            .Select(i =>
            {
                var stockOnHand = i.CurrentStock;
                if (postAsOfDeltas != null && postAsOfDeltas.TryGetValue(i.Id, out var delta))
                {
                    stockOnHand = i.CurrentStock - delta;
                }

                return new StockSummaryLineDto(
                    i.Id,
                    i.ItemCode,
                    i.ItemName,
                    i.CategoryName,
                    FormatUnitSymbol(i.UnitSymbol, i.UnitName),
                    stockOnHand,
                    i.MinimumStock,
                    i.ReorderLevel,
                    i.PurchaseRate,
                    stockOnHand * i.PurchaseRate);
            })
            .Where(l => !request.HideZeroQoh || l.CurrentStock != 0)
            .ToList();

        return new StockSummaryReportDto(
            DateTime.UtcNow,
            asOfDate,
            lines.Count,
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

        var lines = transactions
            .Select(t => new StockMovementLineDto(
                t.TransactionDate,
                t.ReferenceNo,
                t.TransactionType.ToString(),
                t.ItemCode,
                t.ItemName,
                t.Name,
                t.TransactionType is InventoryTransactionType.StockIn or InventoryTransactionType.Opening
                    ? t.Quantity
                    : 0m,
                t.TransactionType == InventoryTransactionType.StockOut ? t.Quantity : 0m,
                t.TransactionType == InventoryTransactionType.Adjustment ? t.Quantity : 0m,
                t.UnitCost,
                t.TotalCost,
                t.StackNo,
                t.LotNo,
                t.Notes))
            .ToList();

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
}
