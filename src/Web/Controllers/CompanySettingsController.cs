using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Settings.View")]
public class CompanySettingsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Settings";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/company-settings")]
public class CompanySettingsApiController : ControllerBase
{
    private readonly ICompanySettingsService _companySettingsService;

    public CompanySettingsApiController(ICompanySettingsService companySettingsService)
    {
        _companySettingsService = companySettingsService;
    }

    [HttpGet]
    [RequirePermission("Settings.View")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _companySettingsService.GetSettingsAsync(cancellationToken);
            return settings is null ? NotFound() : Ok(settings);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut]
    [RequirePermission("Settings.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(
        [FromBody] CompanySettingsSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new CompanySettingsSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _companySettingsService.UpdateSettingsAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new CompanySettingsSaveResult(false, ex.Message, null));
        }
    }
}
