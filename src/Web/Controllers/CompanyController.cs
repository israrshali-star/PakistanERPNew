using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.Interfaces.Services;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[Route("api/[controller]")]
public class CompanyController : Controller
{
    private readonly ICompanyService _companyService;

    public CompanyController(ICompanyService companyService)
    {
        _companyService = companyService;
    }

    [HttpGet("list")]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var companies = await _companyService.GetUserCompaniesAsync(cancellationToken);
        return Ok(companies);
    }

    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken cancellationToken)
    {
        var company = await _companyService.GetCurrentCompanyAsync(cancellationToken);
        if (company is null)
        {
            return NotFound(new { message = "No company selected." });
        }

        return Ok(company);
    }

    [HttpPost("switch/{companyId:int}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Switch(int companyId, CancellationToken cancellationToken)
    {
        var success = await _companyService.SetCurrentCompanyAsync(companyId, cancellationToken);
        if (!success)
        {
            return BadRequest(new { message = "You do not have access to this company." });
        }

        return Ok(new { companyId });
    }
}
