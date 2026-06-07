using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Services;

public class AuditLogService : IAuditLogService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;

    public AuditLogService(IUnitOfWork unitOfWork, ICurrentCompanyService currentCompany)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
    }

    public async Task<DataTableResponse<AuditLogListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<AuditLog>()
            .Query()
            .Where(x => x.CompanyId == companyId || x.CompanyId == null);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(x =>
                x.Action.Contains(term) ||
                (x.TableName != null && x.TableName.Contains(term)) ||
                (x.RecordId != null && x.RecordId.Contains(term)) ||
                (x.UserName != null && x.UserName.Contains(term)) ||
                (x.IPAddress != null && x.IPAddress.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(x => new AuditLogListItemDto(
                x.Id,
                x.CreatedAt,
                x.Action,
                x.TableName,
                x.RecordId,
                x.UserName,
                x.IPAddress,
                x.Company != null ? x.Company.CompanyName : "Global"))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<AuditLogListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<AuditLogDetailDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<AuditLog>()
            .Query()
            .Where(x => x.Id == id && (x.CompanyId == companyId || x.CompanyId == null))
            .Select(x => new AuditLogDetailDto(
                x.Id,
                x.CreatedAt,
                x.Action,
                x.TableName,
                x.RecordId,
                x.UserId,
                x.UserName,
                x.IPAddress,
                x.CompanyId,
                x.Company != null ? x.Company.CompanyName : "Global",
                x.OldValue,
                x.NewValue))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static IQueryable<AuditLog> ApplyOrdering(IQueryable<AuditLog> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
            1 => desc ? query.OrderByDescending(x => x.Action) : query.OrderBy(x => x.Action),
            2 => desc ? query.OrderByDescending(x => x.TableName) : query.OrderBy(x => x.TableName),
            3 => desc ? query.OrderByDescending(x => x.RecordId) : query.OrderBy(x => x.RecordId),
            4 => desc ? query.OrderByDescending(x => x.UserName) : query.OrderBy(x => x.UserName),
            _ => query.OrderByDescending(x => x.CreatedAt)
        };
    }
}
