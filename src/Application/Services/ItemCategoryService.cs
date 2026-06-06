using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using System.Text.Json;

namespace PakistanAccountingERP.Application.Services;

public class ItemCategoryService : IItemCategoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILogger<ItemCategoryService> _logger;

    public ItemCategoryService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ILogger<ItemCategoryService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<DataTableResponse<ItemCategoryListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<ItemCategory>()
            .Query()
            .Where(c => c.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(c =>
                c.Name.Contains(term)
                || (c.Description != null && c.Description.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(c => new ItemCategoryListItemDto(
                c.Id,
                c.Name,
                c.Description,
                c.Items.Count))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<ItemCategoryListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<ItemCategoryDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<ItemCategory>()
            .Query()
            .Where(c => c.Id == id && c.CompanyId == companyId)
            .Select(c => new ItemCategoryDto(c.Id, c.Name, c.Description, c.Items.Count))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ItemCategorySaveResult> CreateAsync(
        ItemCategorySaveRequest request,
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

        var entity = new ItemCategory
        {
            CompanyId = companyId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.UserName
        };

        try
        {
            await _unitOfWork.Repository<ItemCategory>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create item category");
            return new ItemCategorySaveResult(false, "Could not save category.", null);
        }

        await TryAuditAsync("Create", entity.Id.ToString(), null, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new ItemCategorySaveResult(true, null, dto);
    }

    public async Task<ItemCategorySaveResult> UpdateAsync(
        ItemCategorySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Id.HasValue)
        {
            return new ItemCategorySaveResult(false, "Category id is required.", null);
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

        var entity = await _unitOfWork.Repository<ItemCategory>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(c => c.Id == request.Id.Value && c.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new ItemCategorySaveResult(false, "Category not found.", null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new { entity.Name, entity.Description });

        entity.Name = request.Name.Trim();
        entity.Description = request.Description?.Trim();
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.UserName;

        try
        {
            _unitOfWork.Repository<ItemCategory>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update item category {CategoryId}", request.Id);
            return new ItemCategorySaveResult(false, "Could not update category.", null);
        }

        await TryAuditAsync("Update", entity.Id.ToString(), oldSnapshot, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new ItemCategorySaveResult(true, null, dto);
    }

    public async Task<ItemCategorySaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var entity = await _unitOfWork.Repository<ItemCategory>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new ItemCategorySaveResult(false, "Category not found.", null);
        }

        var hasItems = await _unitOfWork.Repository<Item>()
            .Query()
            .AnyAsync(i => i.ItemCategoryId == id, cancellationToken);

        if (hasItems)
        {
            return new ItemCategorySaveResult(
                false,
                "Cannot delete this category because items are assigned to it.",
                null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new { entity.Name });
        _unitOfWork.Repository<ItemCategory>().Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await TryAuditAsync("Delete", id.ToString(), oldSnapshot, null, cancellationToken);
        return new ItemCategorySaveResult(true, "Category deleted successfully.", null);
    }

    private async Task<ItemCategorySaveResult> ValidateSaveRequestAsync(
        ItemCategorySaveRequest request,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new ItemCategorySaveResult(false, "Category name is required.", null);
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var duplicateName = await _unitOfWork.Repository<ItemCategory>()
            .Query()
            .AnyAsync(c =>
                c.CompanyId == companyId
                && c.Name == request.Name.Trim()
                && (!excludeId.HasValue || c.Id != excludeId.Value),
                cancellationToken);

        if (duplicateName)
        {
            return new ItemCategorySaveResult(false, "A category with this name already exists.", null);
        }

        return new ItemCategorySaveResult(true, null, null);
    }

    private bool TryGetCompanyId(out int companyId, out ItemCategorySaveResult? error)
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
            error = new ItemCategorySaveResult(false, ex.Message, null);
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
            await _auditService.LogAsync(action, "ItemCategories", recordId, oldValue, newValue, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for item category {RecordId}", recordId);
        }
    }

    private static IQueryable<ItemCategory> ApplyOrdering(IQueryable<ItemCategory> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            1 => desc ? query.OrderByDescending(c => c.Description) : query.OrderBy(c => c.Description),
            2 => desc ? query.OrderByDescending(c => c.Items.Count) : query.OrderBy(c => c.Items.Count),
            _ => query.OrderBy(c => c.Name)
        };
    }
}
