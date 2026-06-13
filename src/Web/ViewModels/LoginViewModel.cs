using System.ComponentModel.DataAnnotations;

namespace PakistanAccountingERP.Web.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    [Display(Name = "Company")]
    public int CompanyId { get; set; }

    public bool RequireCompanySelection { get; set; }

    public string? ReturnUrl { get; set; }

    public List<CompanyOptionViewModel> Companies { get; set; } = [];
}
