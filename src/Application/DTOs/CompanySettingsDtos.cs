namespace PakistanAccountingERP.Application.DTOs;

public record CompanySettingsDto(
    int CompanyId,
    string CompanyName,
    string? Address,
    string? NTN,
    int? ProvinceId,
    string? ProvinceName,
    string? Phone,
    string? Email,
    string? GodownEmail,
    string? FbrPostUrl,
    bool HasApiToken,
    bool FbrLiveMode,
    int? TaxSettingId,
    string TaxGroupName,
    decimal SalesTaxRate,
    decimal UnregisteredSalesTaxRate,
    CompanyEmailSettingsDto EmailSettings,
    CompanyWhatsAppSettingsDto WhatsAppSettings);

public class CompanySettingsSaveRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? NTN { get; set; }
    public int? ProvinceId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? GodownEmail { get; set; }
    public string? FbrPostUrl { get; set; }
    public string? ApiToken { get; set; }
    public bool ClearApiToken { get; set; }
    public decimal SalesTaxRate { get; set; } = 18m;
    public decimal UnregisteredSalesTaxRate { get; set; } = 18m;

    public bool? SmtpEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public bool? SmtpUseSsl { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool ClearSmtpPassword { get; set; }
    public string? SmtpFromEmail { get; set; }
    public string? SmtpFromName { get; set; }

    public bool? WhatsAppEnabled { get; set; }
    public string? WhatsAppApiUrl { get; set; }
    public string? WhatsAppAccessToken { get; set; }
    public bool ClearWhatsAppAccessToken { get; set; }
    public string? WhatsAppPhoneNumberId { get; set; }
}

public record CompanySettingsSaveResult(bool Success, string? Message, CompanySettingsDto? Settings);
