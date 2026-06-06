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
    public string? FbrPostUrl { get; set; }
    public string? ApiToken { get; set; }
    public string? LogoPath { get; set; }
    public bool IsDefault { get; set; }

    public Province? Province { get; set; }
    public ICollection<UserCompany> UserCompanies { get; set; } = new List<UserCompany>();
}
