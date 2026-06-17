namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;

public class Company : AuditableEntity
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? NTN { get; set; }
    public int? ProvinceId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? GodownEmail { get; set; }
    public string? FbrPostUrl { get; set; }
    public string? ApiToken { get; set; }
    public string? LogoPath { get; set; }
    public bool IsDefault { get; set; }

    public bool SmtpEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string? SmtpFromEmail { get; set; }
    public string? SmtpFromName { get; set; }

    public bool WhatsAppEnabled { get; set; }
    public string? WhatsAppApiUrl { get; set; }
    public string? WhatsAppAccessToken { get; set; }
    public string? WhatsAppPhoneNumberId { get; set; }

    public Province? Province { get; set; }
    public ICollection<UserCompany> UserCompanies { get; set; } = new List<UserCompany>();
}
