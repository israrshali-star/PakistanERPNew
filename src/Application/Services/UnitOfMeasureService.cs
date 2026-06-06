using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using System.Text.Json;

namespace PakistanAccountingERP.Application.Services;

public class UnitOfMeasureService : IUnitOfMeasureService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly ILogger<UnitOfMeasureService> _logger;

    public UnitOfMeasureService(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        ILogger<UnitOfMeasureService> logger)
    {
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<DataTableResponse<UnitOfMeasureListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<UnitOfMeasure>().Query();

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(u =>
                u.Name.Contains(term)
                || (u.Symbol != null && u.Symbol.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(u => new UnitOfMeasureListItemDto(
                u.Id,
                u.Name,
                u.Symbol,
                u.Items.Count))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<UnitOfMeasureListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<UnitOfMeasureDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.Repository<UnitOfMeasure>()
            .Query()
            .Where(u => u.Id == id)
            .Select(u => new UnitOfMeasureDto(u.Id, u.Name, u.Symbol, u.Items.Count))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<UnitOfMeasureSaveResult> CreateAsync(
        UnitOfMeasureSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateSaveRequestAsync(request, null, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        var entity = new UnitOfMeasure
        {
            Name = request.Name.Trim(),
            Symbol = request.Symbol?.Trim()
        };

        try
        {
            await _unitOfWork.Repository<UnitOfMeasure>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create unit of measure");
            return new UnitOfMeasureSaveResult(false, "Could not save unit of measure.", null);
        }

        await TryAuditAsync("Create", entity.Id.ToString(), null, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new UnitOfMeasureSaveResult(true, null, dto);
    }

    public async Task<UnitOfMeasureSaveResult> UpdateAsync(
        UnitOfMeasureSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Id.HasValue)
        {
            return new UnitOfMeasureSaveResult(false, "Unit id is required.", null);
        }

        var validation = await ValidateSaveRequestAsync(request, request.Id.Value, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        var entity = await _unitOfWork.Repository<UnitOfMeasure>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(u => u.Id == request.Id.Value, cancellationToken);

        if (entity is null)
        {
            return new UnitOfMeasureSaveResult(false, "Unit of measure not found.", null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new { entity.Name, entity.Symbol });

        entity.Name = request.Name.Trim();
        entity.Symbol = request.Symbol?.Trim();

        try
        {
            _unitOfWork.Repository<UnitOfMeasure>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update unit of measure {UnitId}", request.Id);
            return new UnitOfMeasureSaveResult(false, "Could not update unit of measure.", null);
        }

        await TryAuditAsync("Update", entity.Id.ToString(), oldSnapshot, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new UnitOfMeasureSaveResult(true, null, dto);
    }

    public async Task<UnitOfMeasureSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<UnitOfMeasure>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (entity is null)
        {
            return new UnitOfMeasureSaveResult(false, "Unit of measure not found.", null);
        }

        var hasItems = await _unitOfWork.Repository<Item>()
            .Query()
            .AnyAsync(i => i.UnitOfMeasureId == id, cancellationToken);

        if (hasItems)
        {
            return new UnitOfMeasureSaveResult(
                false,
                "Cannot delete this unit because items are using it.",
                null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new { entity.Name, entity.Symbol });
        _unitOfWork.Repository<UnitOfMeasure>().Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await TryAuditAsync("Delete", id.ToString(), oldSnapshot, null, cancellationToken);
        return new UnitOfMeasureSaveResult(true, "Unit of measure deleted successfully.", null);
    }

    private async Task<UnitOfMeasureSaveResult> ValidateSaveRequestAsync(
        UnitOfMeasureSaveRequest request,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new UnitOfMeasureSaveResult(false, "Name is required.", null);
        }

        var name = request.Name.Trim();
        var symbol = request.Symbol?.Trim();

        var duplicateName = await _unitOfWork.Repository<UnitOfMeasure>()
            .Query()
            .AnyAsync(u =>
                u.Name == name
                && (!excludeId.HasValue || u.Id != excludeId.Value),
                cancellationToken);

        if (duplicateName)
        {
            return new UnitOfMeasureSaveResult(false, "A unit with this name already exists.", null);
        }

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var duplicateSymbol = await _unitOfWork.Repository<UnitOfMeasure>()
                .Query()
                .AnyAsync(u =>
                    u.Symbol == symbol
                    && (!excludeId.HasValue || u.Id != excludeId.Value),
                    cancellationToken);

            if (duplicateSymbol)
            {
                return new UnitOfMeasureSaveResult(false, "A unit with this symbol already exists.", null);
            }
        }

        return new UnitOfMeasureSaveResult(true, null, null);
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
            await _auditService.LogAsync(action, "UnitsOfMeasure", recordId, oldValue, newValue, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for unit of measure {RecordId}", recordId);
        }
    }

    private static IQueryable<UnitOfMeasure> ApplyOrdering(IQueryable<UnitOfMeasure> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(u => u.Name) : query.OrderBy(u => u.Name),
            1 => desc ? query.OrderByDescending(u => u.Symbol) : query.OrderBy(u => u.Symbol),
            2 => desc ? query.OrderByDescending(u => u.Items.Count) : query.OrderBy(u => u.Items.Count),
            _ => query.OrderBy(u => u.Name)
        };
    }
}
