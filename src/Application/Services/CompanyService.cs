using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using System.Text.Json;

namespace PakistanAccountingERP.Application.Services;

public class CompanyService : ICompanyService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly IAuditService _auditService;
    private readonly ILogger<CompanyService> _logger;

    public CompanyService(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        ICurrentCompanyService currentCompany,
        IAuditService auditService,
        ILogger<CompanyService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CompanyDto>> GetUserCompaniesAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Array.Empty<CompanyDto>();
        }

        if (IsSuperAdmin())
        {
            return await GetAllActiveCompaniesAsync(cancellationToken);
        }

        return await _unitOfWork.Repository<UserCompany>()
            .Query()
            .Where(uc => uc.UserId == _currentUser.UserId && !uc.Company.IsDeleted)
            .OrderByDescending(uc => uc.Company.IsDefault)
            .ThenBy(uc => uc.Company.CompanyName)
            .Select(uc => new CompanyDto(
                uc.Company.Id,
                uc.Company.CompanyName,
                uc.Company.NTN,
                uc.Company.IsDefault))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CompanyListItemDto>> GetManageableCompaniesAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Array.Empty<CompanyListItemDto>();
        }

        if (IsSuperAdmin())
        {
            return await _unitOfWork.Repository<Company>()
                .Query()
                .Where(c => !c.IsDeleted)
                .OrderByDescending(c => c.IsDefault)
                .ThenBy(c => c.CompanyName)
                .Select(c => new CompanyListItemDto(
                    c.Id,
                    c.CompanyName,
                    c.NTN,
                    c.Province != null ? c.Province.Name : null,
                    c.Phone,
                    c.Email,
                    c.IsDefault))
                .ToListAsync(cancellationToken);
        }

        return await _unitOfWork.Repository<UserCompany>()
            .Query()
            .Where(uc => uc.UserId == _currentUser.UserId && !uc.Company.IsDeleted)
            .OrderByDescending(uc => uc.Company.IsDefault)
            .ThenBy(uc => uc.Company.CompanyName)
            .Select(uc => new CompanyListItemDto(
                uc.Company.Id,
                uc.Company.CompanyName,
                uc.Company.NTN,
                uc.Company.Province != null ? uc.Company.Province.Name : null,
                uc.Company.Phone,
                uc.Company.Email,
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
            .Where(c => c.Id == _currentCompany.CompanyId.Value && !c.IsDeleted)
            .Select(c => new CompanyDto(c.Id, c.CompanyName, c.NTN, c.IsDefault))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CompanyDetailDto?> GetCompanyDetailAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        if (!await UserHasAccessAsync(companyId, cancellationToken))
        {
            return null;
        }

        return await _unitOfWork.Repository<Company>()
            .Query()
            .Where(c => c.Id == companyId && !c.IsDeleted)
            .Select(c => new CompanyDetailDto(
                c.Id,
                c.CompanyName,
                c.Address,
                c.NTN,
                c.ProvinceId,
                c.Province != null ? c.Province.Name : null,
                c.Phone,
                c.Email,
                c.IsDefault))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> SetCurrentCompanyAsync(
        int companyId,
        bool lockSession = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return false;
        }

        if (_currentCompany.IsCompanyLocked
            && _currentCompany.CompanyId.HasValue
            && _currentCompany.CompanyId.Value != companyId)
        {
            return false;
        }

        var hasAccess = await UserHasAccessAsync(companyId, cancellationToken);

        if (!hasAccess)
        {
            return false;
        }

        await _currentCompany.SetCompanyAsync(companyId, cancellationToken);

        if (lockSession)
        {
            await _currentCompany.LockCompanyAsync(cancellationToken);
        }

        return true;
    }

    public async Task<CompanySaveResult> CreateCompanyAsync(
        CompanySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSaveRequest(request, isCreate: true);
        if (!validation.Success)
        {
            return validation;
        }

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return new CompanySaveResult(false, "You must be signed in to create a company.", null);
        }

        if (request.ProvinceId.HasValue
            && !await _unitOfWork.Repository<Province>().Query()
                .AnyAsync(p => p.Id == request.ProvinceId.Value, cancellationToken))
        {
            return new CompanySaveResult(false, "Selected province is not valid.", null);
        }

        var duplicateName = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => !c.IsDeleted && c.CompanyName == request.CompanyName.Trim(), cancellationToken);

        if (duplicateName)
        {
            return new CompanySaveResult(false, "A company with this name already exists.", null);
        }

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? _currentUser.UserId;

        var company = new Company
        {
            CompanyName = request.CompanyName.Trim(),
            Address = request.Address?.Trim(),
            NTN = request.NTN?.Trim(),
            ProvinceId = request.ProvinceId,
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            IsDefault = request.IsDefault,
            CreatedAt = now,
            CreatedBy = userName
        };

        await _unitOfWork.Repository<Company>().AddAsync(company, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create company {CompanyName}", request.CompanyName);
            return new CompanySaveResult(false, "Could not create company.", null);
        }

        await _unitOfWork.Repository<UserCompany>().AddAsync(new UserCompany
        {
            UserId = _currentUser.UserId,
            CompanyId = company.Id
        }, cancellationToken);

        await _unitOfWork.Repository<TaxSetting>().AddAsync(new TaxSetting
        {
            CompanyId = company.Id,
            GroupName = "Standard Rate",
            Description = "Default Pakistan sales tax rates",
            SalesTaxRate = 18m,
            UnregisteredSalesTaxRate = 22m,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = userName
        }, cancellationToken);

        var accounts = DefaultChartOfAccounts.CreateForCompany(company.Id, userName, now);
        await _unitOfWork.Repository<ChartOfAccount>().AddRangeAsync(accounts, cancellationToken);

        if (request.IsDefault)
        {
            await ClearDefaultFlagExceptAsync(company.Id, cancellationToken);
            company.IsDefault = true;
            _unitOfWork.Repository<Company>().Update(company);
        }
        else if (!await AnyDefaultCompanyExistsAsync(cancellationToken))
        {
            company.IsDefault = true;
            _unitOfWork.Repository<Company>().Update(company);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to provision company {CompanyId}", company.Id);
            return new CompanySaveResult(false, "Company was created but setup could not be completed.", null);
        }

        await TryAuditAsync("Create", company.Id.ToString(), null, JsonSerializer.Serialize(request), cancellationToken);

        var detail = await GetCompanyDetailAsync(company.Id, cancellationToken);
        return new CompanySaveResult(true, "Company created successfully.", detail);
    }

    public async Task<CompanySaveResult> UpdateCompanyAsync(
        CompanySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSaveRequest(request, isCreate: false);
        if (!validation.Success)
        {
            return validation;
        }

        if (!request.Id.HasValue)
        {
            return new CompanySaveResult(false, "Company id is required.", null);
        }

        if (!await UserHasAccessAsync(request.Id.Value, cancellationToken))
        {
            return new CompanySaveResult(false, "You do not have access to this company.", null);
        }

        if (request.ProvinceId.HasValue
            && !await _unitOfWork.Repository<Province>().Query()
                .AnyAsync(p => p.Id == request.ProvinceId.Value, cancellationToken))
        {
            return new CompanySaveResult(false, "Selected province is not valid.", null);
        }

        var company = await _unitOfWork.Repository<Company>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(c => c.Id == request.Id.Value && !c.IsDeleted, cancellationToken);

        if (company is null)
        {
            return new CompanySaveResult(false, "Company not found.", null);
        }

        var duplicateName = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => !c.IsDeleted
                           && c.Id != company.Id
                           && c.CompanyName == request.CompanyName.Trim(),
                cancellationToken);

        if (duplicateName)
        {
            return new CompanySaveResult(false, "A company with this name already exists.", null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            company.CompanyName,
            company.NTN,
            company.IsDefault
        });

        company.CompanyName = request.CompanyName.Trim();
        company.Address = request.Address?.Trim();
        company.NTN = request.NTN?.Trim();
        company.ProvinceId = request.ProvinceId;
        company.Phone = request.Phone?.Trim();
        company.Email = request.Email?.Trim();
        company.UpdatedAt = DateTime.UtcNow;
        company.UpdatedBy = _currentUser.UserName;

        if (request.IsDefault)
        {
            await ClearDefaultFlagExceptAsync(company.Id, cancellationToken);
            company.IsDefault = true;
        }
        else if (company.IsDefault)
        {
            var otherDefaultExists = await _unitOfWork.Repository<Company>()
                .Query()
                .AnyAsync(c => !c.IsDeleted && c.Id != company.Id && c.IsDefault, cancellationToken);

            if (!otherDefaultExists)
            {
                return new CompanySaveResult(
                    false,
                    "At least one company must remain as default. Set another company as default first.",
                    null);
            }

            company.IsDefault = false;
        }

        _unitOfWork.Repository<Company>().Update(company);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update company {CompanyId}", company.Id);
            return new CompanySaveResult(false, "Could not update company.", null);
        }

        await TryAuditAsync("Update", company.Id.ToString(), oldSnapshot, JsonSerializer.Serialize(request), cancellationToken);

        var detail = await GetCompanyDetailAsync(company.Id, cancellationToken);
        return new CompanySaveResult(true, "Company updated successfully.", detail);
    }

    public async Task<CompanySaveResult> DeleteCompanyAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        if (!await UserHasAccessAsync(companyId, cancellationToken))
        {
            return new CompanySaveResult(false, "You do not have access to this company.", null);
        }

        var company = await _unitOfWork.Repository<Company>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(c => c.Id == companyId && !c.IsDeleted, cancellationToken);

        if (company is null)
        {
            return new CompanySaveResult(false, "Company not found.", null);
        }

        var activeCompanyCount = await _unitOfWork.Repository<Company>()
            .Query()
            .CountAsync(c => !c.IsDeleted, cancellationToken);

        if (activeCompanyCount <= 1)
        {
            return new CompanySaveResult(false, "Cannot delete the only remaining company.", null);
        }

        if (await CompanyHasBusinessDataAsync(companyId, cancellationToken))
        {
            return new CompanySaveResult(
                false,
                "This company has transactions or master data and cannot be deleted.",
                null);
        }

        var wasDefault = company.IsDefault;
        _unitOfWork.Repository<Company>().Remove(company);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to delete company {CompanyId}", companyId);
            return new CompanySaveResult(false, "Could not delete company.", null);
        }

        if (wasDefault)
        {
            var nextDefault = await _unitOfWork.Repository<Company>()
                .Query(asNoTracking: false)
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (nextDefault is not null)
            {
                nextDefault.IsDefault = true;
                nextDefault.UpdatedAt = DateTime.UtcNow;
                nextDefault.UpdatedBy = _currentUser.UserName;
                _unitOfWork.Repository<Company>().Update(nextDefault);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        if (_currentCompany.CompanyId == companyId)
        {
            await _currentCompany.ClearCompanyAsync(cancellationToken);
        }

        await TryAuditAsync("Delete", companyId.ToString(), JsonSerializer.Serialize(new { company.CompanyName }), null, cancellationToken);

        return new CompanySaveResult(true, "Company deleted successfully.", null);
    }

    public async Task<CompanySaveResult> SetDefaultCompanyAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        if (!await UserHasAccessAsync(companyId, cancellationToken))
        {
            return new CompanySaveResult(false, "You do not have access to this company.", null);
        }

        var company = await _unitOfWork.Repository<Company>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(c => c.Id == companyId && !c.IsDeleted, cancellationToken);

        if (company is null)
        {
            return new CompanySaveResult(false, "Company not found.", null);
        }

        await ClearDefaultFlagExceptAsync(companyId, cancellationToken);
        company.IsDefault = true;
        company.UpdatedAt = DateTime.UtcNow;
        company.UpdatedBy = _currentUser.UserName;
        _unitOfWork.Repository<Company>().Update(company);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to set default company {CompanyId}", companyId);
            return new CompanySaveResult(false, "Could not set default company.", null);
        }

        await TryAuditAsync("SetDefault", companyId.ToString(), null, null, cancellationToken);

        var detail = await GetCompanyDetailAsync(companyId, cancellationToken);
        return new CompanySaveResult(true, "Default company updated.", detail);
    }

    private async Task<bool> UserHasAccessAsync(int companyId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return false;
        }

        if (IsSuperAdmin())
        {
            return await _unitOfWork.Repository<Company>()
                .Query()
                .AnyAsync(c => c.Id == companyId && !c.IsDeleted, cancellationToken);
        }

        return await _unitOfWork.Repository<UserCompany>()
            .Query()
            .AnyAsync(uc => uc.UserId == _currentUser.UserId
                            && uc.CompanyId == companyId
                            && !uc.Company.IsDeleted,
                cancellationToken);
    }

    private async Task<IReadOnlyList<CompanyDto>> GetAllActiveCompaniesAsync(CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<Company>()
            .Query()
            .Where(c => !c.IsDeleted)
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.CompanyName)
            .Select(c => new CompanyDto(c.Id, c.CompanyName, c.NTN, c.IsDefault))
            .ToListAsync(cancellationToken);

    private bool IsSuperAdmin() =>
        _currentUser.Roles.Any(r => string.Equals(r, "SuperAdmin", StringComparison.OrdinalIgnoreCase));

    private async Task<bool> AnyDefaultCompanyExistsAsync(CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => !c.IsDeleted && c.IsDefault, cancellationToken);

    private async Task ClearDefaultFlagExceptAsync(int companyId, CancellationToken cancellationToken)
    {
        var defaults = await _unitOfWork.Repository<Company>()
            .Query(asNoTracking: false)
            .Where(c => !c.IsDeleted && c.IsDefault && c.Id != companyId)
            .ToListAsync(cancellationToken);

        foreach (var item in defaults)
        {
            item.IsDefault = false;
            item.UpdatedAt = DateTime.UtcNow;
            item.UpdatedBy = _currentUser.UserName;
            _unitOfWork.Repository<Company>().Update(item);
        }
    }

    private static async Task<bool> CompanyHasBusinessDataAsync(
        int companyId,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        return await unitOfWork.Repository<Customer>().Query().AnyAsync(c => c.CompanyId == companyId, cancellationToken)
               || await unitOfWork.Repository<Vendor>().Query().AnyAsync(v => v.CompanyId == companyId, cancellationToken)
               || await unitOfWork.Repository<Item>().Query().AnyAsync(i => i.CompanyId == companyId, cancellationToken)
               || await unitOfWork.Repository<SalesInvoice>().Query().AnyAsync(s => s.CompanyId == companyId, cancellationToken)
               || await unitOfWork.Repository<VendorBill>().Query().AnyAsync(b => b.CompanyId == companyId, cancellationToken)
               || await unitOfWork.Repository<JournalEntry>().Query().AnyAsync(j => j.CompanyId == companyId, cancellationToken)
               || await unitOfWork.Repository<Bank>().Query().AnyAsync(b => b.CompanyId == companyId, cancellationToken)
               || await unitOfWork.Repository<Warehouse>().Query().AnyAsync(w => w.CompanyId == companyId, cancellationToken)
               || await unitOfWork.Repository<CustomerReceipt>().Query().AnyAsync(r => r.CompanyId == companyId, cancellationToken)
               || await unitOfWork.Repository<VendorPayment>().Query().AnyAsync(p => p.CompanyId == companyId, cancellationToken)
               || await unitOfWork.Repository<InventoryTransaction>().Query().AnyAsync(t => t.CompanyId == companyId, cancellationToken);
    }

    private Task<bool> CompanyHasBusinessDataAsync(int companyId, CancellationToken cancellationToken) =>
        CompanyHasBusinessDataAsync(companyId, _unitOfWork, cancellationToken);

    private static CompanySaveResult ValidateSaveRequest(CompanySaveRequest request, bool isCreate)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName))
        {
            return new CompanySaveResult(false, "Company name is required.", null);
        }

        if (!isCreate && (!request.Id.HasValue || request.Id.Value <= 0))
        {
            return new CompanySaveResult(false, "Company id is required.", null);
        }

        return new CompanySaveResult(true, null, null);
    }

    private async Task TryAuditAsync(
        string action,
        string entityId,
        string? oldValues,
        string? newValues,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditService.LogAsync(
                ReferenceTypes.Company,
                entityId,
                action,
                oldValues,
                newValues,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for company {EntityId}", entityId);
        }
    }
}
