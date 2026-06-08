using System.ComponentModel.DataAnnotations;

namespace PakistanAccountingERP.Web.ViewModels;

public class SelectCompanyViewModel
{
    [Required(ErrorMessage = "Please select a company.")]
    [Display(Name = "Company")]
    public int CompanyId { get; set; }

    public string? ReturnUrl { get; set; }

    public List<CompanyOptionViewModel> Companies { get; set; } = [];
}

public class CompanyOptionViewModel
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
