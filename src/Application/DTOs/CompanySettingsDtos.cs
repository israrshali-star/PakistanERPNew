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
    string? FbrPostUrl,
    bool HasApiToken,
    bool FbrLiveMode,
    int? TaxSettingId,
    string TaxGroupName,
    decimal SalesTaxRate,
    decimal UnregisteredSalesTaxRate);

public class CompanySettingsSaveRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? NTN { get; set; }
    public int? ProvinceId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? FbrPostUrl { get; set; }
    public string? ApiToken { get; set; }
    public bool ClearApiToken { get; set; }
    public decimal SalesTaxRate { get; set; } = 18m;
    public decimal UnregisteredSalesTaxRate { get; set; } = 18m;
}

public record CompanySettingsSaveResult(bool Success, string? Message, CompanySettingsDto? Settings);
