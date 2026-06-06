using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Services;

public class LookupService : ILookupService
{
    private readonly IUnitOfWork _unitOfWork;

    public LookupService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<LookupDto>> GetProvincesAsync(CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.Repository<Province>()
            .Query()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new LookupDto(p.Id, p.Name, p.Code))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LookupDto>> GetUnitsOfMeasureAsync(CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.Repository<UnitOfMeasure>()
            .Query()
            .OrderBy(u => u.Name)
            .Select(u => new LookupDto(u.Id, u.Name, u.Symbol))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScenarioTypeDto>> GetScenarioTypesAsync(CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.Repository<ScenarioType>()
            .Query()
            .OrderBy(s => s.Code)
            .Select(s => new ScenarioTypeDto(s.ScenarioId, s.Code, s.Description))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccountTypeDto>> GetAccountTypesAsync(CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.Repository<AccountType>()
            .Query()
            .Where(t => t.IsActive)
            .OrderBy(t => t.TypeId)
            .Select(t => new AccountTypeDto(t.TypeId, t.TypeCode, t.TypeName))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubAccountTypeDto>> GetSubAccountTypesAsync(
        int? typeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<SubAccountType>().Query();

        if (typeId.HasValue)
        {
            query = query.Where(s => s.TypeId == typeId.Value);
        }

        return await query
            .OrderBy(s => s.SubTypeCode)
            .Select(s => new SubAccountTypeDto(s.SubTypeId, s.TypeId, s.SubTypeCode, s.SubTypeName))
            .ToListAsync(cancellationToken);
    }
}
