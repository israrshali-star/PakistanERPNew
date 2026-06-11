using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/company")]
public class CompanyController : ControllerBase
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

    [HttpGet("manage")]
    [RequirePermission("Settings.View")]
    public async Task<IActionResult> ManageList(CancellationToken cancellationToken)
    {
        var companies = await _companyService.GetManageableCompaniesAsync(cancellationToken);
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

    [HttpGet("{companyId:int}")]
    [RequirePermission("Settings.View")]
    public async Task<IActionResult> Get(int companyId, CancellationToken cancellationToken)
    {
        var company = await _companyService.GetCompanyDetailAsync(companyId, cancellationToken);
        return company is null ? NotFound() : Ok(company);
    }

    [HttpPost]
    [RequirePermission("Settings.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] CompanySaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new CompanySaveResult(false, "Invalid request body.", null));
        }

        var result = await _companyService.CreateCompanyAsync(request, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{companyId:int}")]
    [RequirePermission("Settings.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(
        int companyId,
        [FromBody] CompanySaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new CompanySaveResult(false, "Invalid request body.", null));
        }

        request = request with { Id = companyId };
        var result = await _companyService.UpdateCompanyAsync(request, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("{companyId:int}")]
    [RequirePermission("Settings.Delete")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(int companyId, CancellationToken cancellationToken)
    {
        var result = await _companyService.DeleteCompanyAsync(companyId, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{companyId:int}/set-default")]
    [RequirePermission("Settings.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SetDefault(int companyId, CancellationToken cancellationToken)
    {
        var result = await _companyService.SetDefaultCompanyAsync(companyId, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("switch/{companyId:int}")]
    [IgnoreAntiforgeryToken]
    public IActionResult Switch(int companyId)
    {
        return StatusCode(
            StatusCodes.Status403Forbidden,
            new { message = "Company cannot be changed during an active session. Sign out and log in again to switch companies." });
    }
}
