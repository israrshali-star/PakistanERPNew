using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.Interfaces.Services;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[Route("api/[controller]")]
public class LookupController : Controller
{
    private readonly ILookupService _lookupService;

    public LookupController(ILookupService lookupService)
    {
        _lookupService = lookupService;
    }

    [HttpGet("account-types")]
    public async Task<IActionResult> AccountTypes(CancellationToken cancellationToken)
    {
        return Ok(await _lookupService.GetAccountTypesAsync(cancellationToken));
    }

    [HttpGet("sub-account-types")]
    public async Task<IActionResult> SubAccountTypes(int? typeId, CancellationToken cancellationToken)
    {
        return Ok(await _lookupService.GetSubAccountTypesAsync(typeId, cancellationToken));
    }

    [HttpGet("provinces")]
    public async Task<IActionResult> Provinces(CancellationToken cancellationToken)
    {
        return Ok(await _lookupService.GetProvincesAsync(cancellationToken));
    }

    [HttpGet("scenario-types")]
    public async Task<IActionResult> ScenarioTypes(CancellationToken cancellationToken)
    {
        return Ok(await _lookupService.GetScenarioTypesAsync(cancellationToken));
    }

    [HttpGet("units-of-measure")]
    public async Task<IActionResult> UnitsOfMeasure(CancellationToken cancellationToken)
    {
        return Ok(await _lookupService.GetUnitsOfMeasureAsync(cancellationToken));
    }
}
