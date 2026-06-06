namespace PakistanAccountingERP.Domain.Entities;

public class UserCompany
{
    public string UserId { get; set; } = string.Empty;
    public int CompanyId { get; set; }

    public Company Company { get; set; } = null!;
}
