using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Services;

public class CompanyService : ICompanyService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentCompanyService _currentCompany;

    public CompanyService(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        ICurrentCompanyService currentCompany)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public async Task<IReadOnlyList<CompanyDto>> GetUserCompaniesAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Array.Empty<CompanyDto>();
        }

        return await _unitOfWork.Repository<UserCompany>()
            .Query()
            .Where(uc => uc.UserId == _currentUser.UserId)
            .OrderBy(uc => uc.Company.CompanyName)
            .Select(uc => new CompanyDto(
                uc.Company.Id,
                uc.Company.CompanyName,
                uc.Company.NTN,
                uc.Company.IsDefault))
            .ToListAsync(cancellationToken);
    }

    public async Task<CompanyDto?> GetCurrentCompanyAsync(CancellationToken cancellationToken = default)
    {
        if (!_currentCompany.CompanyId.HasValue)
        {
            return null;
        }

        return await _unitOfWork.Repository<Company>()
            .Query()
            .Where(c => c.Id == _currentCompany.CompanyId.Value)
            .Select(c => new CompanyDto(c.Id, c.CompanyName, c.NTN, c.IsDefault))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> SetCurrentCompanyAsync(int companyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return false;
        }

        var hasAccess = await _unitOfWork.Repository<UserCompany>()
            .Query()
            .AnyAsync(uc => uc.UserId == _currentUser.UserId && uc.CompanyId == companyId, cancellationToken);

        if (!hasAccess)
        {
            return false;
        }

        await _currentCompany.SetCompanyAsync(companyId, cancellationToken);
        return true;
    }
}
