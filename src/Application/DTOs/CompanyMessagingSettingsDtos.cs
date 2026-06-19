namespace PakistanAccountingERP.Application.DTOs;

public record ResolvedSmtpSettings(
    bool Enabled,
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    string Password,
    string FromEmail,
    string FromName);

public record CompanyEmailSettingsDto(
    bool SmtpEnabled,
    string? SmtpHost,
    int SmtpPort,
    bool SmtpUseSsl,
    string? SmtpUsername,
    bool HasSmtpPassword,
    string? SmtpFromEmail,
    string? SmtpFromName,
    bool SmtpConfigured);

public record CompanyWhatsAppSettingsDto(
    bool WhatsAppEnabled,
    string? WhatsAppApiUrl,
    bool HasWhatsAppAccessToken,
    string? WhatsAppPhoneNumberId,
    bool WhatsAppConfigured);
