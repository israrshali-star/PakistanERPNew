using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Services;

public class CompanyMessagingSettingsService : ICompanyMessagingSettingsService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;

    public CompanyMessagingSettingsService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
    }

    public async Task<ResolvedSmtpSettings?> GetSmtpSettingsAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.CompanyId;
        if (!companyId.HasValue)
        {
            return null;
        }

        var company = await _unitOfWork.Repository<Company>()
            .Query()
            .Where(c => c.Id == companyId.Value)
            .Select(c => new
            {
                c.SmtpEnabled,
                c.SmtpHost,
                c.SmtpPort,
                c.SmtpUseSsl,
                c.SmtpUsername,
                c.SmtpPassword,
                c.SmtpFromEmail,
                c.SmtpFromName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (company is null || !IsSmtpComplete(
                company.SmtpEnabled,
                company.SmtpHost,
                company.SmtpFromEmail,
                company.SmtpUsername,
                company.SmtpPassword))
        {
            return null;
        }

        return new ResolvedSmtpSettings(
            company.SmtpEnabled,
            company.SmtpHost!.Trim(),
            company.SmtpPort > 0 ? company.SmtpPort : 587,
            company.SmtpUseSsl,
            company.SmtpUsername!.Trim(),
            NormalizePassword(company.SmtpPassword),
            company.SmtpFromEmail!.Trim(),
            string.IsNullOrWhiteSpace(company.SmtpFromName)
                ? "Pakistan Accounting ERP"
                : company.SmtpFromName.Trim());
    }

    public Task<bool> IsSmtpConfiguredAsync(CancellationToken cancellationToken = default) =>
        GetSmtpSettingsAsync(cancellationToken).ContinueWith(
            task => task.Result is not null,
            cancellationToken,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    public async Task<bool> IsWhatsAppApiConfiguredAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.CompanyId;
        if (!companyId.HasValue)
        {
            return false;
        }

        return await _unitOfWork.Repository<Company>()
            .Query()
            .Where(c => c.Id == companyId.Value)
            .Select(c =>
                c.WhatsAppEnabled
                && c.WhatsAppAccessToken != null
                && c.WhatsAppAccessToken != ""
                && c.WhatsAppPhoneNumberId != null
                && c.WhatsAppPhoneNumberId != "")
            .FirstOrDefaultAsync(cancellationToken);
    }

    internal static bool IsSmtpComplete(
        bool enabled,
        string? host,
        string? fromEmail,
        string? username,
        string? password) =>
        enabled
        && !string.IsNullOrWhiteSpace(host)
        && !string.IsNullOrWhiteSpace(fromEmail)
        && !string.IsNullOrWhiteSpace(username)
        && !string.IsNullOrWhiteSpace(NormalizePassword(password));

    internal static bool IsWhatsAppComplete(
        bool enabled,
        string? accessToken,
        string? phoneNumberId) =>
        enabled
        && !string.IsNullOrWhiteSpace(accessToken)
        && !string.IsNullOrWhiteSpace(phoneNumberId);

    internal static string NormalizePassword(string? password) =>
        (password ?? string.Empty).Replace(" ", string.Empty);
}
