using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Services;

public partial class ItemService : IItemService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILogger<ItemService> _logger;

    public ItemService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ILogger<ItemService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<DataTableResponse<ItemListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(i =>
                i.ItemCode.Contains(term)
                || i.ItemName.Contains(term)
                || (i.HSCode != null && i.HSCode.Contains(term))
                || (i.Barcode != null && i.Barcode.Contains(term))
                || (i.ItemCategory != null && i.ItemCategory.Name.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(i => new ItemListItemDto(
                i.Id,
                i.ItemCode,
                i.ItemName,
                i.ItemType.ToString(),
                i.ItemCategory != null ? i.ItemCategory.Name : null,
                i.UnitOfMeasure.Symbol ?? i.UnitOfMeasure.Name,
                i.SaleRate,
                i.CurrentStock,
                i.IsActive))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<ItemListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<ItemDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.Id == id && i.CompanyId == companyId)
            .Select(i => new ItemDto(
                i.Id,
                i.ItemType,
                i.ItemCode,
                i.ItemName,
                i.StackNo,
                i.LotNo,
                i.Description,
                i.HSCode,
                i.Barcode,
                i.UnitOfMeasureId,
                i.UnitOfMeasure.Symbol ?? i.UnitOfMeasure.Name,
                i.ItemCategoryId,
                i.ItemCategory != null ? i.ItemCategory.Name : null,
                i.PurchaseRate,
                i.SaleRate,
                i.MinimumStock,
                i.ReorderLevel,
                i.CurrentStock,
                i.CostingMethod,
                i.IsActive,
                i.SalesInvoiceLines.Any()
                    || i.VendorBillLines.Any()
                    || i.InventoryTransactions.Any()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<NextItemCodeDto> GenerateNextItemCodeAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var prefix = AppConstants.ItemCodePrefix;

        var codes = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.ItemCode.StartsWith(prefix))
            .Select(i => i.ItemCode)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var code in codes)
        {
            var match = ItemCodeRegex().Match(code);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var seq))
            {
                max = Math.Max(max, seq);
            }
        }

        return new NextItemCodeDto($"{prefix}{(max + 1):D4}");
    }

    public async Task<IReadOnlyList<ItemCategoryLookupDto>> GetCategoryLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<ItemCategory>()
            .Query()
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.Name)
            .Select(c => new ItemCategoryLookupDto(c.Id, c.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<ItemSaveResult> CreateAsync(
        ItemSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateSaveRequestAsync(request, null, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var now = DateTime.UtcNow;
        var openingStock = request.OpeningStock ?? 0m;

        var entity = new Item
        {
            CompanyId = companyId,
            ItemType = request.ItemType,
            ItemCode = request.ItemCode.Trim(),
            ItemName = request.ItemName.Trim(),
            StackNo = request.StackNo.Trim(),
            LotNo = request.LotNo.Trim(),
            Description = request.Description?.Trim(),
            HSCode = request.HSCode?.Trim(),
            Barcode = request.Barcode?.Trim(),
            UnitOfMeasureId = request.UnitOfMeasureId,
            ItemCategoryId = request.ItemCategoryId,
            PurchaseRate = request.PurchaseRate,
            SaleRate = request.SaleRate,
            MinimumStock = request.MinimumStock,
            ReorderLevel = request.ReorderLevel,
            CurrentStock = openingStock,
            CostingMethod = request.CostingMethod,
            IsActive = request.IsActive,
            CreatedAt = now,
            CreatedBy = _currentUser.UserName
        };

        try
        {
            await _unitOfWork.Repository<Item>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create item");
            return new ItemSaveResult(false, "Could not save item. Check item code is unique.", null);
        }

        await TryAuditAsync("Create", entity.Id.ToString(), null, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new ItemSaveResult(true, null, dto);
    }

    public async Task<ItemSaveResult> UpdateAsync(
        ItemSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Id.HasValue)
        {
            return new ItemSaveResult(false, "Item id is required.", null);
        }

        var validation = await ValidateSaveRequestAsync(request, request.Id.Value, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var entity = await _unitOfWork.Repository<Item>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(i => i.Id == request.Id.Value && i.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new ItemSaveResult(false, "Item not found.", null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            entity.ItemCode,
            entity.ItemName,
            entity.SaleRate,
            entity.IsActive
        });

        entity.ItemType = request.ItemType;
        entity.ItemCode = request.ItemCode.Trim();
        entity.ItemName = request.ItemName.Trim();
        entity.StackNo = request.StackNo.Trim();
        entity.LotNo = request.LotNo.Trim();
        entity.Description = request.Description?.Trim();
        entity.HSCode = request.HSCode?.Trim();
        entity.Barcode = request.Barcode?.Trim();
        entity.UnitOfMeasureId = request.UnitOfMeasureId;
        entity.ItemCategoryId = request.ItemCategoryId;
        entity.PurchaseRate = request.PurchaseRate;
        entity.SaleRate = request.SaleRate;
        entity.MinimumStock = request.MinimumStock;
        entity.ReorderLevel = request.ReorderLevel;
        entity.CostingMethod = request.CostingMethod;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.UserName;

        try
        {
            _unitOfWork.Repository<Item>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update item {ItemId}", request.Id);
            return new ItemSaveResult(false, "Could not update item. Check item code is unique.", null);
        }

        await TryAuditAsync("Update", entity.Id.ToString(), oldSnapshot, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new ItemSaveResult(true, null, dto);
    }

    public async Task<ItemSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var entity = await _unitOfWork.Repository<Item>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new ItemSaveResult(false, "Item not found.", null);
        }

        var hasUsage = await _unitOfWork.Repository<SalesInvoiceLine>()
            .Query()
            .AnyAsync(l => l.ItemId == id, cancellationToken)
            || await _unitOfWork.Repository<VendorBillLine>()
                .Query()
                .AnyAsync(l => l.ItemId == id, cancellationToken)
            || await _unitOfWork.Repository<InventoryTransaction>()
                .Query()
                .AnyAsync(t => t.ItemId == id, cancellationToken);

        if (hasUsage)
        {
            return new ItemSaveResult(
                false,
                "Cannot delete this item because it is used on invoices, bills, or inventory transactions.",
                null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new { entity.ItemCode, entity.ItemName });
        _unitOfWork.Repository<Item>().Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await TryAuditAsync("Delete", id.ToString(), oldSnapshot, null, cancellationToken);
        return new ItemSaveResult(true, "Item deleted successfully.", null);
    }

    private async Task<ItemSaveResult> ValidateSaveRequestAsync(
        ItemSaveRequest request,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ItemCode))
        {
            return new ItemSaveResult(false, "Item code is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.ItemName))
        {
            return new ItemSaveResult(false, "Item name is required.", null);
        }

        if (request.UnitOfMeasureId <= 0)
        {
            return new ItemSaveResult(false, "Unit of measure is required.", null);
        }

        if (request.SaleRate < 0 || request.PurchaseRate < 0)
        {
            return new ItemSaveResult(false, "Rates cannot be negative.", null);
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var uomExists = await _unitOfWork.Repository<UnitOfMeasure>()
            .Query()
            .AnyAsync(u => u.Id == request.UnitOfMeasureId, cancellationToken);

        if (!uomExists)
        {
            return new ItemSaveResult(false, "Selected unit of measure is not valid.", null);
        }

        if (request.ItemCategoryId.HasValue)
        {
            var categoryExists = await _unitOfWork.Repository<ItemCategory>()
                .Query()
                .AnyAsync(c => c.Id == request.ItemCategoryId && c.CompanyId == companyId, cancellationToken);

            if (!categoryExists)
            {
                return new ItemSaveResult(false, "Selected category is not valid.", null);
            }
        }

        var duplicateCode = await _unitOfWork.Repository<Item>()
            .Query()
            .AnyAsync(i =>
                i.CompanyId == companyId
                && i.ItemCode == request.ItemCode.Trim()
                && (!excludeId.HasValue || i.Id != excludeId.Value),
                cancellationToken);

        if (duplicateCode)
        {
            return new ItemSaveResult(false, "Item code already exists.", null);
        }

        return new ItemSaveResult(true, null, null);
    }

    private bool TryGetCompanyId(out int companyId, out ItemSaveResult? error)
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
            error = new ItemSaveResult(false, ex.Message, null);
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
            await _auditService.LogAsync(action, "Items", recordId, oldValue, newValue, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for item {RecordId}", recordId);
        }
    }

    private static IQueryable<Item> ApplyOrdering(IQueryable<Item> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(i => i.ItemCode) : query.OrderBy(i => i.ItemCode),
            1 => desc ? query.OrderByDescending(i => i.ItemName) : query.OrderBy(i => i.ItemName),
            2 => desc ? query.OrderByDescending(i => i.ItemType) : query.OrderBy(i => i.ItemType),
            3 => desc ? query.OrderByDescending(i => i.ItemCategory!.Name) : query.OrderBy(i => i.ItemCategory!.Name),
            4 => desc ? query.OrderByDescending(i => i.UnitOfMeasure.Symbol) : query.OrderBy(i => i.UnitOfMeasure.Symbol),
            5 => desc ? query.OrderByDescending(i => i.SaleRate) : query.OrderBy(i => i.SaleRate),
            6 => desc ? query.OrderByDescending(i => i.CurrentStock) : query.OrderBy(i => i.CurrentStock),
            7 => desc ? query.OrderByDescending(i => i.IsActive) : query.OrderBy(i => i.IsActive),
            _ => query.OrderBy(i => i.ItemName)
        };
    }

    [GeneratedRegex(@"^ITEM-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ItemCodeRegex();
}
