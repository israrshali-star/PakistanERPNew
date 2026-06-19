using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ICompanyMessagingSettingsService
{
    Task<ResolvedSmtpSettings?> GetSmtpSettingsAsync(CancellationToken cancellationToken = default);

    Task<bool> IsSmtpConfiguredAsync(CancellationToken cancellationToken = default);

    Task<bool> IsWhatsAppApiConfiguredAsync(CancellationToken cancellationToken = default);
}
