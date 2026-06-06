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

public partial class WarehouseService : IWarehouseService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILogger<WarehouseService> _logger;

    public WarehouseService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ILogger<WarehouseService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<DataTableResponse<WarehouseListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<Warehouse>()
            .Query()
            .Where(w => w.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(w =>
                w.Code.Contains(term)
                || w.Name.Contains(term)
                || (w.Address != null && w.Address.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(w => new WarehouseListItemDto(
                w.Id,
                w.Code,
                w.Name,
                w.Address,
                w.IsActive,
                w.InventoryTransactions.Count))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<WarehouseListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<WarehouseDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Warehouse>()
            .Query()
            .Where(w => w.Id == id && w.CompanyId == companyId)
            .Select(w => new WarehouseDto(
                w.Id,
                w.Code,
                w.Name,
                w.Address,
                w.IsActive,
                w.InventoryTransactions.Count))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<NextWarehouseCodeDto> GenerateNextCodeAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var prefix = AppConstants.WarehouseCodePrefix;

        var codes = await _unitOfWork.Repository<Warehouse>()
            .Query()
            .Where(w => w.CompanyId == companyId && w.Code.StartsWith(prefix))
            .Select(w => w.Code)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var code in codes)
        {
            var match = WarehouseCodeRegex().Match(code);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var seq))
            {
                max = Math.Max(max, seq);
            }
        }

        return new NextWarehouseCodeDto($"{prefix}{(max + 1):D4}");
    }

    public async Task<WarehouseSaveResult> CreateAsync(
        WarehouseSaveRequest request,
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

        var entity = new Warehouse
        {
            CompanyId = companyId,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Address = request.Address?.Trim(),
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.UserName
        };

        try
        {
            await _unitOfWork.Repository<Warehouse>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create warehouse");
            return new WarehouseSaveResult(false, "Could not save warehouse. Check code is unique.", null);
        }

        await TryAuditAsync("Create", entity.Id.ToString(), null, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new WarehouseSaveResult(true, null, dto);
    }

    public async Task<WarehouseSaveResult> UpdateAsync(
        WarehouseSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Id.HasValue)
        {
            return new WarehouseSaveResult(false, "Warehouse id is required.", null);
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

        var entity = await _unitOfWork.Repository<Warehouse>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(w => w.Id == request.Id.Value && w.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new WarehouseSaveResult(false, "Warehouse not found.", null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new { entity.Code, entity.Name, entity.IsActive });

        entity.Code = request.Code.Trim();
        entity.Name = request.Name.Trim();
        entity.Address = request.Address?.Trim();
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.UserName;

        try
        {
            _unitOfWork.Repository<Warehouse>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update warehouse {WarehouseId}", request.Id);
            return new WarehouseSaveResult(false, "Could not update warehouse.", null);
        }

        await TryAuditAsync("Update", entity.Id.ToString(), oldSnapshot, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new WarehouseSaveResult(true, null, dto);
    }

    public async Task<WarehouseSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var entity = await _unitOfWork.Repository<Warehouse>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(w => w.Id == id && w.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new WarehouseSaveResult(false, "Warehouse not found.", null);
        }

        var hasTransactions = await _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .AnyAsync(t => t.WarehouseId == id, cancellationToken);

        if (hasTransactions)
        {
            return new WarehouseSaveResult(
                false,
                "Cannot delete this warehouse because stock transactions exist.",
                null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new { entity.Code, entity.Name });
        _unitOfWork.Repository<Warehouse>().Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await TryAuditAsync("Delete", id.ToString(), oldSnapshot, null, cancellationToken);
        return new WarehouseSaveResult(true, "Warehouse deleted successfully.", null);
    }

    private async Task<WarehouseSaveResult> ValidateSaveRequestAsync(
        WarehouseSaveRequest request,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return new WarehouseSaveResult(false, "Warehouse code is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new WarehouseSaveResult(false, "Warehouse name is required.", null);
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var duplicateCode = await _unitOfWork.Repository<Warehouse>()
            .Query()
            .AnyAsync(w =>
                w.CompanyId == companyId
                && w.Code == request.Code.Trim()
                && (!excludeId.HasValue || w.Id != excludeId.Value),
                cancellationToken);

        if (duplicateCode)
        {
            return new WarehouseSaveResult(false, "Warehouse code already exists.", null);
        }

        return new WarehouseSaveResult(true, null, null);
    }

    private bool TryGetCompanyId(out int companyId, out WarehouseSaveResult? error)
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
            error = new WarehouseSaveResult(false, ex.Message, null);
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
            await _auditService.LogAsync(action, "Warehouses", recordId, oldValue, newValue, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for warehouse {RecordId}", recordId);
        }
    }

    private static IQueryable<Warehouse> ApplyOrdering(IQueryable<Warehouse> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(w => w.Code) : query.OrderBy(w => w.Code),
            1 => desc ? query.OrderByDescending(w => w.Name) : query.OrderBy(w => w.Name),
            2 => desc ? query.OrderByDescending(w => w.Address) : query.OrderBy(w => w.Address),
            3 => desc ? query.OrderByDescending(w => w.IsActive) : query.OrderBy(w => w.IsActive),
            4 => desc ? query.OrderByDescending(w => w.InventoryTransactions.Count) : query.OrderBy(w => w.InventoryTransactions.Count),
            _ => query.OrderBy(w => w.Name)
        };
    }

    [GeneratedRegex(@"^WH-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex WarehouseCodeRegex();
}
