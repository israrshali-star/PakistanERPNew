using Microsoft.AspNetCore.Identity;

namespace PakistanAccountingERP.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Domain.Entities.UserCompany> UserCompanies { get; set; } = new List<Domain.Entities.UserCompany>();
}
