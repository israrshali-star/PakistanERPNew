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

public class CompanySettingsService : ICompanySettingsService
{
    private const string DefaultWhatsAppApiUrl = "https://graph.facebook.com/v21.0/";

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
                c.GodownEmail,
                c.FbrPostUrl,
                HasApiToken = c.ApiToken != null && c.ApiToken != "",
                c.SmtpEnabled,
                c.SmtpHost,
                c.SmtpPort,
                c.SmtpUseSsl,
                c.SmtpUsername,
                HasSmtpPassword = c.SmtpPassword != null && c.SmtpPassword != "",
                c.SmtpFromEmail,
                c.SmtpFromName,
                c.WhatsAppEnabled,
                c.WhatsAppApiUrl,
                HasWhatsAppAccessToken = c.WhatsAppAccessToken != null && c.WhatsAppAccessToken != "",
                c.WhatsAppPhoneNumberId
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

        var emailSettings = new CompanyEmailSettingsDto(
            company.SmtpEnabled,
            company.SmtpHost,
            company.SmtpPort > 0 ? company.SmtpPort : 587,
            company.SmtpUseSsl,
            company.SmtpUsername,
            company.HasSmtpPassword,
            company.SmtpFromEmail,
            company.SmtpFromName,
            CompanyMessagingSettingsService.IsSmtpComplete(
                company.SmtpEnabled,
                company.SmtpHost,
                company.SmtpFromEmail,
                company.SmtpUsername,
                company.HasSmtpPassword ? "***" : null));

        var whatsAppSettings = new CompanyWhatsAppSettingsDto(
            company.WhatsAppEnabled,
            company.WhatsAppApiUrl,
            company.HasWhatsAppAccessToken,
            company.WhatsAppPhoneNumberId,
            CompanyMessagingSettingsService.IsWhatsAppComplete(
                company.WhatsAppEnabled,
                company.HasWhatsAppAccessToken ? "***" : null,
                company.WhatsAppPhoneNumberId));

        return new CompanySettingsDto(
            company.Id,
            company.CompanyName,
            company.Address,
            company.NTN,
            company.ProvinceId,
            company.ProvinceName,
            company.Phone,
            company.Email,
            company.GodownEmail,
            company.FbrPostUrl,
            company.HasApiToken,
            fbrLive,
            tax?.Id,
            tax?.GroupName ?? "Standard Rate",
            tax?.SalesTaxRate ?? 18m,
            tax?.UnregisteredSalesTaxRate ?? 18m,
            emailSettings,
            whatsAppSettings);
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
            HasApiToken = !string.IsNullOrEmpty(company.ApiToken),
            company.SmtpEnabled,
            company.SmtpHost,
            HasSmtpPassword = !string.IsNullOrEmpty(company.SmtpPassword),
            company.WhatsAppEnabled,
            HasWhatsAppAccessToken = !string.IsNullOrEmpty(company.WhatsAppAccessToken)
        });

        company.CompanyName = request.CompanyName.Trim();
        company.Address = request.Address?.Trim();
        company.NTN = request.NTN?.Trim();
        company.ProvinceId = request.ProvinceId;
        company.Phone = request.Phone?.Trim();
        company.Email = request.Email?.Trim();
        if (companyId == TradeInvoiceLayout.TradeInvoiceCompanyId)
        {
            company.GodownEmail = request.GodownEmail?.Trim();
        }
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

        if (IsSuperAdmin())
        {
            ApplyMessagingSettings(company, request);
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

    private static void ApplyMessagingSettings(Company company, CompanySettingsSaveRequest request)
    {
        if (request.SmtpEnabled.HasValue)
        {
            company.SmtpEnabled = request.SmtpEnabled.Value;
        }

        if (request.SmtpHost is not null)
        {
            company.SmtpHost = string.IsNullOrWhiteSpace(request.SmtpHost) ? null : request.SmtpHost.Trim();
        }

        if (request.SmtpPort.HasValue)
        {
            company.SmtpPort = request.SmtpPort.Value > 0 ? request.SmtpPort.Value : 587;
        }

        if (request.SmtpUseSsl.HasValue)
        {
            company.SmtpUseSsl = request.SmtpUseSsl.Value;
        }

        if (request.SmtpUsername is not null)
        {
            company.SmtpUsername = string.IsNullOrWhiteSpace(request.SmtpUsername) ? null : request.SmtpUsername.Trim();
        }

        if (request.ClearSmtpPassword)
        {
            company.SmtpPassword = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.SmtpPassword))
        {
            company.SmtpPassword = request.SmtpPassword.Trim();
        }

        if (request.SmtpFromEmail is not null)
        {
            company.SmtpFromEmail = string.IsNullOrWhiteSpace(request.SmtpFromEmail) ? null : request.SmtpFromEmail.Trim();
        }

        if (request.SmtpFromName is not null)
        {
            company.SmtpFromName = string.IsNullOrWhiteSpace(request.SmtpFromName) ? null : request.SmtpFromName.Trim();
        }

        if (request.WhatsAppEnabled.HasValue)
        {
            company.WhatsAppEnabled = request.WhatsAppEnabled.Value;
        }

        if (request.WhatsAppApiUrl is not null)
        {
            company.WhatsAppApiUrl = string.IsNullOrWhiteSpace(request.WhatsAppApiUrl)
                ? DefaultWhatsAppApiUrl
                : request.WhatsAppApiUrl.Trim();
        }

        if (request.ClearWhatsAppAccessToken)
        {
            company.WhatsAppAccessToken = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.WhatsAppAccessToken))
        {
            company.WhatsAppAccessToken = request.WhatsAppAccessToken.Trim();
        }

        if (request.WhatsAppPhoneNumberId is not null)
        {
            company.WhatsAppPhoneNumberId = string.IsNullOrWhiteSpace(request.WhatsAppPhoneNumberId)
                ? null
                : request.WhatsAppPhoneNumberId.Trim();
        }
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

        if (request.SmtpPort is < 1 or > 65535)
        {
            return new CompanySettingsSaveResult(false, "SMTP port must be between 1 and 65535.", null);
        }

        if (!string.IsNullOrWhiteSpace(request.WhatsAppApiUrl)
            && !Uri.TryCreate(request.WhatsAppApiUrl.Trim(), UriKind.Absolute, out _))
        {
            return new CompanySettingsSaveResult(false, "WhatsApp API URL must be a valid absolute URL.", null);
        }

        return new CompanySettingsSaveResult(true, null, null);
    }

    private bool IsSuperAdmin() =>
        _currentUser.Roles.Any(r => string.Equals(r, "SuperAdmin", StringComparison.OrdinalIgnoreCase));

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
