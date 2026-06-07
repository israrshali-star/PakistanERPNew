using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using System.Text.Json;

namespace PakistanAccountingERP.Application.Services;

public class FiscalYearService : IFiscalYearService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILogger<FiscalYearService> _logger;

    public FiscalYearService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ILogger<FiscalYearService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<DataTableResponse<FiscalYearListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<FiscalYear>()
            .Query()
            .Where(x => x.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(x => x.Code.Contains(term) || x.Name.Contains(term));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(x => new FiscalYearListItemDto(
                x.Id,
                x.Code,
                x.Name,
                x.StartDate,
                x.EndDate,
                x.IsActive,
                x.IsClosed))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<FiscalYearListItemDto>(request.Draw, recordsTotal, recordsFiltered, rows);
    }

    public async Task<FiscalYearDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<FiscalYear>()
            .Query()
            .Where(x => x.Id == id && x.CompanyId == companyId)
            .Select(MapDto())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FiscalYearSaveResult> CreateAsync(
        FiscalYearSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(request, null, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var code = string.IsNullOrWhiteSpace(request.Code)
            ? GenerateCodeFromDates(request.StartDate, request.EndDate)
            : request.Code.Trim();

        var entity = new FiscalYear
        {
            CompanyId = companyId,
            Code = code,
            Name = request.Name.Trim(),
            StartDate = request.StartDate.Date,
            EndDate = request.EndDate.Date,
            IsActive = request.IsActive,
            IsClosed = request.IsClosed,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.UserName
        };

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (entity.IsActive)
            {
                await DeactivateOthersAsync(companyId, null, cancellationToken);
            }

            await _unitOfWork.Repository<FiscalYear>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to create fiscal year");
            return new FiscalYearSaveResult(false, "Could not create fiscal year.", null);
        }

        await TryAuditAsync("Create", entity.Id.ToString(), null, JsonSerializer.Serialize(entity), cancellationToken);
        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new FiscalYearSaveResult(true, null, dto);
    }

    public async Task<FiscalYearSaveResult> UpdateAsync(
        FiscalYearSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Id.HasValue)
        {
            return new FiscalYearSaveResult(false, "Fiscal year id is required.", null);
        }

        var validation = await ValidateAsync(request, request.Id.Value, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var entity = await _unitOfWork.Repository<FiscalYear>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(x => x.Id == request.Id.Value && x.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new FiscalYearSaveResult(false, "Fiscal year not found.", null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            entity.Code,
            entity.Name,
            entity.StartDate,
            entity.EndDate,
            entity.IsActive,
            entity.IsClosed
        });

        entity.Code = string.IsNullOrWhiteSpace(request.Code)
            ? GenerateCodeFromDates(request.StartDate, request.EndDate)
            : request.Code.Trim();
        entity.Name = request.Name.Trim();
        entity.StartDate = request.StartDate.Date;
        entity.EndDate = request.EndDate.Date;
        entity.IsActive = request.IsActive;
        entity.IsClosed = request.IsClosed;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.UserName;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (entity.IsActive)
            {
                await DeactivateOthersAsync(companyId, entity.Id, cancellationToken);
            }

            _unitOfWork.Repository<FiscalYear>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to update fiscal year {FiscalYearId}", entity.Id);
            return new FiscalYearSaveResult(false, "Could not update fiscal year.", null);
        }

        await TryAuditAsync("Update", entity.Id.ToString(), oldSnapshot, JsonSerializer.Serialize(entity), cancellationToken);
        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new FiscalYearSaveResult(true, null, dto);
    }

    public async Task<FiscalYearActionResult> SetActiveAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var entity = await _unitOfWork.Repository<FiscalYear>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new FiscalYearActionResult(false, "Fiscal year not found.", null);
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        await DeactivateOthersAsync(companyId, id, cancellationToken);
        entity.IsActive = true;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.UserName;
        _unitOfWork.Repository<FiscalYear>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await TryAuditAsync("SetActive", id.ToString(), null, "{\"isActive\":true}", cancellationToken);
        var dto = await GetByIdAsync(id, cancellationToken);
        return new FiscalYearActionResult(true, "Active fiscal year updated.", dto);
    }

    public async Task<FiscalYearActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var entity = await _unitOfWork.Repository<FiscalYear>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new FiscalYearActionResult(false, "Fiscal year not found.", null);
        }

        if (entity.IsClosed)
        {
            return new FiscalYearActionResult(false, "Closed fiscal year cannot be deleted.", null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new { entity.Code, entity.Name });
        _unitOfWork.Repository<FiscalYear>().Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await TryAuditAsync("Delete", id.ToString(), oldSnapshot, null, cancellationToken);

        return new FiscalYearActionResult(true, "Fiscal year deleted successfully.", null);
    }

    private async Task<FiscalYearSaveResult> ValidateAsync(
        FiscalYearSaveRequest request,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new FiscalYearSaveResult(false, "Fiscal year name is required.", null);
        }

        if (request.StartDate.Date > request.EndDate.Date)
        {
            return new FiscalYearSaveResult(false, "Start date must be before end date.", null);
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var code = string.IsNullOrWhiteSpace(request.Code)
            ? GenerateCodeFromDates(request.StartDate, request.EndDate)
            : request.Code.Trim();

        var duplicateCode = await _unitOfWork.Repository<FiscalYear>()
            .Query()
            .AnyAsync(x =>
                x.CompanyId == companyId &&
                x.Code == code &&
                (!excludeId.HasValue || x.Id != excludeId.Value),
                cancellationToken);
        if (duplicateCode)
        {
            return new FiscalYearSaveResult(false, "Fiscal year code already exists.", null);
        }

        return new FiscalYearSaveResult(true, null, null);
    }

    private async Task DeactivateOthersAsync(int companyId, int? keepId, CancellationToken cancellationToken)
    {
        var others = await _unitOfWork.Repository<FiscalYear>()
            .Query(asNoTracking: false)
            .Where(x => x.CompanyId == companyId && x.IsActive && (!keepId.HasValue || x.Id != keepId.Value))
            .ToListAsync(cancellationToken);

        foreach (var item in others)
        {
            item.IsActive = false;
            item.UpdatedAt = DateTime.UtcNow;
            item.UpdatedBy = _currentUser.UserName;
            _unitOfWork.Repository<FiscalYear>().Update(item);
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
            await _auditService.LogAsync(action, "FiscalYears", recordId, oldValue, newValue, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for fiscal year {RecordId}", recordId);
        }
    }

    private static string GenerateCodeFromDates(DateTime startDate, DateTime endDate)
    {
        return $"FY{startDate:yyyy}-{endDate:yy}";
    }

    private static IQueryable<FiscalYear> ApplyOrdering(IQueryable<FiscalYear> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(x => x.Code) : query.OrderBy(x => x.Code),
            1 => desc ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
            2 => desc ? query.OrderByDescending(x => x.StartDate) : query.OrderBy(x => x.StartDate),
            3 => desc ? query.OrderByDescending(x => x.EndDate) : query.OrderBy(x => x.EndDate),
            4 => desc ? query.OrderByDescending(x => x.IsActive) : query.OrderBy(x => x.IsActive),
            5 => desc ? query.OrderByDescending(x => x.IsClosed) : query.OrderBy(x => x.IsClosed),
            _ => query.OrderByDescending(x => x.StartDate)
        };
    }

    private static System.Linq.Expressions.Expression<Func<FiscalYear, FiscalYearDto>> MapDto()
    {
        return x => new FiscalYearDto(
            x.Id,
            x.Code,
            x.Name,
            x.StartDate,
            x.EndDate,
            x.IsActive,
            x.IsClosed);
    }
}
