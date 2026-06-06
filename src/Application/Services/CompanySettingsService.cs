using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using System.Text.Json;

namespace PakistanAccountingERP.Application.Services;

public class CompanySettingsService : ICompanySettingsService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILogger<CompanySettingsService> _logger;

    public CompanySettingsService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ILogger<CompanySettingsService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<CompanySettingsDto?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var company = await _unitOfWork.Repository<Company>()
            .Query()
            .Where(c => c.Id == companyId)
            .Select(c => new
            {
                c.Id,
                c.CompanyName,
                c.Address,
                c.NTN,
                c.ProvinceId,
                ProvinceName = c.Province != null ? c.Province.Name : null,
                c.Phone,
                c.Email,
                c.FbrPostUrl,
                HasApiToken = c.ApiToken != null && c.ApiToken != ""
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (company is null)
        {
            return null;
        }

        var tax = await _unitOfWork.Repository<TaxSetting>()
            .Query()
            .Where(t => t.CompanyId == companyId)
            .OrderBy(t => t.Id)
            .Select(t => new { t.Id, t.GroupName, t.SalesTaxRate, t.UnregisteredSalesTaxRate })
            .FirstOrDefaultAsync(cancellationToken);

        var fbrLive = !string.IsNullOrWhiteSpace(company.FbrPostUrl) && company.HasApiToken;

        return new CompanySettingsDto(
            company.Id,
            company.CompanyName,
            company.Address,
            company.NTN,
            company.ProvinceId,
            company.ProvinceName,
            company.Phone,
            company.Email,
            company.FbrPostUrl,
            company.HasApiToken,
            fbrLive,
            tax?.Id,
            tax?.GroupName ?? "Standard Rate",
            tax?.SalesTaxRate ?? 18m,
            tax?.UnregisteredSalesTaxRate ?? 18m);
    }

    public async Task<CompanySettingsSaveResult> UpdateSettingsAsync(
        CompanySettingsSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateRequest(request);
        if (!validation.Success)
        {
            return validation;
        }

        var companyId = _currentCompany.GetRequiredCompanyId();

        var company = await _unitOfWork.Repository<Company>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company is null)
        {
            return new CompanySettingsSaveResult(false, "Company not found.", null);
        }

        if (request.ProvinceId.HasValue)
        {
            var provinceExists = await _unitOfWork.Repository<Province>()
                .Query()
                .AnyAsync(p => p.Id == request.ProvinceId.Value, cancellationToken);

            if (!provinceExists)
            {
                return new CompanySettingsSaveResult(false, "Selected province is not valid.", null);
            }
        }

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            company.CompanyName,
            company.NTN,
            company.FbrPostUrl,
            HasApiToken = !string.IsNullOrEmpty(company.ApiToken)
        });

        company.CompanyName = request.CompanyName.Trim();
        company.Address = request.Address?.Trim();
        company.NTN = request.NTN?.Trim();
        company.ProvinceId = request.ProvinceId;
        company.Phone = request.Phone?.Trim();
        company.Email = request.Email?.Trim();
        company.FbrPostUrl = string.IsNullOrWhiteSpace(request.FbrPostUrl)
            ? null
            : request.FbrPostUrl.Trim();
        company.UpdatedAt = DateTime.UtcNow;
        company.UpdatedBy = _currentUser.UserName;

        if (request.ClearApiToken)
        {
            company.ApiToken = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.ApiToken))
        {
            company.ApiToken = request.ApiToken.Trim();
        }

        var tax = await _unitOfWork.Repository<TaxSetting>()
            .Query(asNoTracking: false)
            .Where(t => t.CompanyId == companyId)
            .OrderBy(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName;

        if (tax is null)
        {
            tax = new TaxSetting
            {
                CompanyId = companyId,
                GroupName = "Standard Rate",
                Description = "Default Pakistan sales tax rates",
                SalesTaxRate = request.SalesTaxRate,
                UnregisteredSalesTaxRate = request.UnregisteredSalesTaxRate,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = userName
            };
            await _unitOfWork.Repository<TaxSetting>().AddAsync(tax, cancellationToken);
        }
        else
        {
            tax.SalesTaxRate = request.SalesTaxRate;
            tax.UnregisteredSalesTaxRate = request.UnregisteredSalesTaxRate;
            tax.UpdatedAt = now;
            tax.UpdatedBy = userName;
            _unitOfWork.Repository<TaxSetting>().Update(tax);
        }

        try
        {
            _unitOfWork.Repository<Company>().Update(company);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update company settings for company {CompanyId}", companyId);
            return new CompanySettingsSaveResult(false, "Could not save settings.", null);
        }

        await TryAuditAsync("Update", companyId.ToString(), oldSnapshot, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetSettingsAsync(cancellationToken);
        return new CompanySettingsSaveResult(true, "Settings saved successfully.", dto);
    }

    private static CompanySettingsSaveResult ValidateRequest(CompanySettingsSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName))
        {
            return new CompanySettingsSaveResult(false, "Company name is required.", null);
        }

        if (request.SalesTaxRate < 0 || request.SalesTaxRate > 100)
        {
            return new CompanySettingsSaveResult(false, "Sales tax rate must be between 0 and 100.", null);
        }

        if (request.UnregisteredSalesTaxRate < 0 || request.UnregisteredSalesTaxRate > 100)
        {
            return new CompanySettingsSaveResult(false, "Unregistered sales tax rate must be between 0 and 100.", null);
        }

        if (!string.IsNullOrWhiteSpace(request.FbrPostUrl)
            && !Uri.TryCreate(request.FbrPostUrl.Trim(), UriKind.Absolute, out _))
        {
            return new CompanySettingsSaveResult(false, "FBR post URL must be a valid absolute URL.", null);
        }

        return new CompanySettingsSaveResult(true, null, null);
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
                ReferenceTypes.CompanySettings,
                entityId,
                action,
                oldValues,
                newValues,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for company settings {EntityId}", entityId);
        }
    }
}
