using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Services;

public partial class InventoryTransactionService : IInventoryTransactionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILogger<InventoryTransactionService> _logger;

    public InventoryTransactionService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ILogger<InventoryTransactionService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<DataTableResponse<InventoryTransactionListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(t =>
                (t.ReferenceNo != null && t.ReferenceNo.Contains(term))
                || t.Item.ItemCode.Contains(term)
                || t.Item.ItemName.Contains(term)
                || t.Warehouse.Name.Contains(term)
                || t.Warehouse.Code.Contains(term));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(t => new InventoryTransactionListItemDto(
                t.Id,
                t.ReferenceNo,
                t.TransactionDate,
                t.TransactionType.ToString(),
                t.Item.ItemCode,
                t.Item.ItemName,
                t.Warehouse.Name,
                t.Quantity,
                t.TotalCost))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<InventoryTransactionListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<InventoryTransactionDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t => t.Id == id && t.CompanyId == companyId)
            .Select(t => new InventoryTransactionDto(
                t.Id,
                t.ReferenceNo,
                t.ItemId,
                t.Item.ItemCode,
                t.Item.ItemName,
                t.WarehouseId,
                t.Warehouse.Code,
                t.Warehouse.Name,
                t.TransactionType,
                t.StackNo,
                t.LotNo,
                t.Quantity,
                t.UnitCost,
                t.TotalCost,
                t.TransactionDate,
                t.Notes))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<NextStockReferenceDto> GenerateNextReferenceAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var prefix = AppConstants.StockTransactionReferencePrefix;

        var references = await _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId && t.ReferenceNo != null && t.ReferenceNo.StartsWith(prefix))
            .Select(t => t.ReferenceNo!)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var reference in references)
        {
            var match = StockReferenceRegex().Match(reference);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var seq))
            {
                max = Math.Max(max, seq);
            }
        }

        return new NextStockReferenceDto($"{prefix}{(max + 1):D4}");
    }

    public async Task<IReadOnlyList<InventoryItemLookupDto>> GetItemLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.IsActive && i.ItemType == ItemType.Goods)
            .OrderBy(i => i.ItemName)
            .Select(i => new InventoryItemLookupDto(
                i.Id,
                i.ItemCode,
                i.ItemName,
                i.CurrentStock,
                i.UnitOfMeasure.Symbol ?? i.UnitOfMeasure.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryWarehouseLookupDto>> GetWarehouseLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Warehouse>()
            .Query()
            .Where(w => w.CompanyId == companyId && w.IsActive)
            .OrderBy(w => w.Name)
            .Select(w => new InventoryWarehouseLookupDto(w.Id, w.Code, w.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<InventoryTransactionSaveResult> CreateAsync(
        InventoryTransactionSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var validation = await ValidateSaveRequestAsync(request, companyId, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        var item = await _unitOfWork.Repository<Item>()
            .Query(asNoTracking: false)
            .FirstAsync(i => i.Id == request.ItemId && i.CompanyId == companyId, cancellationToken);

        var stockDelta = GetStockDelta(request.TransactionType, request.Quantity);
        if (request.TransactionType == InventoryTransactionType.StockOut && item.CurrentStock < request.Quantity)
        {
            return new InventoryTransactionSaveResult(
                false,
                $"Insufficient stock. Available: {item.CurrentStock:N2}",
                null);
        }

        var newStock = item.CurrentStock + stockDelta;
        if (newStock < 0)
        {
            return new InventoryTransactionSaveResult(false, "Stock level cannot go below zero.", null);
        }

        var referenceNo = string.IsNullOrWhiteSpace(request.ReferenceNo)
            ? (await GenerateNextReferenceAsync(cancellationToken)).ReferenceNo
            : request.ReferenceNo.Trim();

        var absQuantity = Math.Abs(request.Quantity);
        var totalCost = Math.Round(absQuantity * request.UnitCost, 2);
        var now = DateTime.UtcNow;

        var entity = new InventoryTransaction
        {
            CompanyId = companyId,
            ItemId = request.ItemId,
            WarehouseId = request.WarehouseId,
            TransactionType = request.TransactionType,
            StackNo = request.StackNo?.Trim(),
            LotNo = request.LotNo?.Trim(),
            Quantity = absQuantity,
            UnitCost = request.UnitCost,
            TotalCost = totalCost,
            TransactionDate = request.TransactionDate.Date,
            ReferenceNo = referenceNo,
            Notes = request.Notes?.Trim(),
            CreatedAt = now,
            CreatedBy = _currentUser.UserName
        };

        item.CurrentStock = newStock;
        item.UpdatedAt = now;
        item.UpdatedBy = _currentUser.UserName;

        try
        {
            await _unitOfWork.Repository<InventoryTransaction>().AddAsync(entity, cancellationToken);
            _unitOfWork.Repository<Item>().Update(item);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create inventory transaction");
            return new InventoryTransactionSaveResult(false, "Could not save stock transaction.", null);
        }

        await TryAuditAsync("Create", entity.Id.ToString(), null, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new InventoryTransactionSaveResult(true, null, dto);
    }

    private async Task<InventoryTransactionSaveResult> ValidateSaveRequestAsync(
        InventoryTransactionSaveRequest request,
        int companyId,
        CancellationToken cancellationToken)
    {
        if (request.ItemId <= 0)
        {
            return new InventoryTransactionSaveResult(false, "Item is required.", null);
        }

        if (request.WarehouseId <= 0)
        {
            return new InventoryTransactionSaveResult(false, "Warehouse is required.", null);
        }

        if (request.TransactionDate == default)
        {
            return new InventoryTransactionSaveResult(false, "Transaction date is required.", null);
        }

        if (request.UnitCost < 0)
        {
            return new InventoryTransactionSaveResult(false, "Unit cost cannot be negative.", null);
        }

        if (request.TransactionType == InventoryTransactionType.Adjustment)
        {
            if (request.Quantity == 0)
            {
                return new InventoryTransactionSaveResult(false, "Adjustment quantity cannot be zero.", null);
            }
        }
        else if (request.Quantity <= 0)
        {
            return new InventoryTransactionSaveResult(false, "Quantity must be greater than zero.", null);
        }

        var itemExists = await _unitOfWork.Repository<Item>()
            .Query()
            .AnyAsync(i => i.Id == request.ItemId && i.CompanyId == companyId && i.IsActive, cancellationToken);

        if (!itemExists)
        {
            return new InventoryTransactionSaveResult(false, "Selected item is not valid.", null);
        }

        var warehouseExists = await _unitOfWork.Repository<Warehouse>()
            .Query()
            .AnyAsync(w => w.Id == request.WarehouseId && w.CompanyId == companyId && w.IsActive, cancellationToken);

        if (!warehouseExists)
        {
            return new InventoryTransactionSaveResult(false, "Selected warehouse is not valid.", null);
        }

        return new InventoryTransactionSaveResult(true, null, null);
    }

    private static decimal GetStockDelta(InventoryTransactionType type, decimal quantity) =>
        type switch
        {
            InventoryTransactionType.StockIn => quantity,
            InventoryTransactionType.Opening => quantity,
            InventoryTransactionType.StockOut => -quantity,
            InventoryTransactionType.Adjustment => quantity,
            _ => 0m
        };

    private bool TryGetCompanyId(out int companyId, out InventoryTransactionSaveResult? error)
    {
        try
        {
            companyId = _currentCompany.GetRequiredCompanyId();
            error = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            companyId = 0;
            error = new InventoryTransactionSaveResult(false, ex.Message, null);
            return false;
        }
    }

    private async Task TryAuditAsync(
        string action,
        string recordId,
        string? oldValue,
        string? newValue,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditService.LogAsync(action, "InventoryTransactions", recordId, oldValue, newValue, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for inventory transaction {RecordId}", recordId);
        }
    }

    private static IQueryable<InventoryTransaction> ApplyOrdering(IQueryable<InventoryTransaction> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(t => t.ReferenceNo) : query.OrderBy(t => t.ReferenceNo),
            1 => desc ? query.OrderByDescending(t => t.TransactionDate) : query.OrderBy(t => t.TransactionDate),
            2 => desc ? query.OrderByDescending(t => t.TransactionType) : query.OrderBy(t => t.TransactionType),
            3 => desc ? query.OrderByDescending(t => t.Item.ItemCode) : query.OrderBy(t => t.Item.ItemCode),
            4 => desc ? query.OrderByDescending(t => t.Warehouse.Name) : query.OrderBy(t => t.Warehouse.Name),
            5 => desc ? query.OrderByDescending(t => t.Quantity) : query.OrderBy(t => t.Quantity),
            6 => desc ? query.OrderByDescending(t => t.TotalCost) : query.OrderBy(t => t.TotalCost),
            _ => query.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id)
        };
    }

    [GeneratedRegex(@"^STK-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex StockReferenceRegex();
}
