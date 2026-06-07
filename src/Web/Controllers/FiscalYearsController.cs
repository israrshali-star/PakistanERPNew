using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Settings.View")]
public class FiscalYearsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Settings";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/fiscal-years")]
public class FiscalYearsApiController : ControllerBase
{
    private readonly IFiscalYearService _fiscalYearService;

    public FiscalYearsApiController(IFiscalYearService fiscalYearService)
    {
        _fiscalYearService = fiscalYearService;
    }

    [HttpGet("datatable")]
    [RequirePermission("Settings.View")]
    public async Task<IActionResult> DataTable(CancellationToken cancellationToken)
    {
        try
        {
            var request = new DataTableRequest(
                Draw: int.TryParse(Request.Query["draw"], out var draw) ? draw : 0,
                Start: int.TryParse(Request.Query["start"], out var start) ? start : 0,
                Length: int.TryParse(Request.Query["length"], out var length) ? length : 10,
                SearchValue: Request.Query["search[value]"],
                OrderColumn: int.TryParse(Request.Query["order[0][column]"], out var col) ? col : 2,
                OrderDirection: Request.Query["order[0][dir]"].ToString());

            var result = await _fiscalYearService.GetDataTableAsync(request, cancellationToken);
            return Ok(new
            {
                draw = result.Draw,
                recordsTotal = result.RecordsTotal,
                recordsFiltered = result.RecordsFiltered,
                data = result.Data
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}")]
    [RequirePermission("Settings.View")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        try
        {
            var fiscalYear = await _fiscalYearService.GetByIdAsync(id, cancellationToken);
            return fiscalYear is null ? NotFound() : Ok(fiscalYear);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission("Settings.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] FiscalYearSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new FiscalYearSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _fiscalYearService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new FiscalYearSaveResult(false, ex.Message, null));
        }
    }

    [HttpPut("{id:int}")]
    [RequirePermission("Settings.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] FiscalYearSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new FiscalYearSaveResult(false, "Invalid request body.", null));
        }

        request.Id = id;

        try
        {
            var result = await _fiscalYearService.UpdateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new FiscalYearSaveResult(false, ex.Message, null));
        }
    }

    [HttpPost("{id:int}/set-active")]
    [RequirePermission("Settings.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SetActive(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _fiscalYearService.SetActiveAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new FiscalYearActionResult(false, ex.Message, null));
        }
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("Settings.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _fiscalYearService.DeleteAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new FiscalYearActionResult(false, ex.Message, null));
        }
    }
}
