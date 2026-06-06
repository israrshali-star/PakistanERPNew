using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Items.View")]
public class UnitsOfMeasureController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Inventory";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/units-of-measure")]
public class UnitsOfMeasureApiController : ControllerBase
{
    private readonly IUnitOfMeasureService _unitOfMeasureService;

    public UnitsOfMeasureApiController(IUnitOfMeasureService unitOfMeasureService)
    {
        _unitOfMeasureService = unitOfMeasureService;
    }

    [HttpGet("datatable")]
    [RequirePermission("Items.View")]
    public async Task<IActionResult> DataTable(CancellationToken cancellationToken)
    {
        var request = new DataTableRequest(
            Draw: int.TryParse(Request.Query["draw"], out var draw) ? draw : 0,
            Start: int.TryParse(Request.Query["start"], out var start) ? start : 0,
            Length: int.TryParse(Request.Query["length"], out var length) ? length : 10,
            SearchValue: Request.Query["search[value]"],
            OrderColumn: int.TryParse(Request.Query["order[0][column]"], out var col) ? col : 0,
            OrderDirection: Request.Query["order[0][dir]"].ToString());

        var result = await _unitOfMeasureService.GetDataTableAsync(request, cancellationToken);
        return Ok(new
        {
            draw = result.Draw,
            recordsTotal = result.RecordsTotal,
            recordsFiltered = result.RecordsFiltered,
            data = result.Data
        });
    }

    [HttpGet("{id:int}")]
    [RequirePermission("Items.View")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var unit = await _unitOfMeasureService.GetByIdAsync(id, cancellationToken);
        return unit is null ? NotFound() : Ok(unit);
    }

    [HttpPost]
    [RequirePermission("Items.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] UnitOfMeasureSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new UnitOfMeasureSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _unitOfMeasureService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new UnitOfMeasureSaveResult(false, ex.Message, null));
        }
    }

    [HttpPut("{id:int}")]
    [RequirePermission("Items.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UnitOfMeasureSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new UnitOfMeasureSaveResult(false, "Invalid request body.", null));
        }

        request.Id = id;

        try
        {
            var result = await _unitOfMeasureService.UpdateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new UnitOfMeasureSaveResult(false, ex.Message, null));
        }
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("Items.Delete")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _unitOfMeasureService.DeleteAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new UnitOfMeasureSaveResult(false, ex.Message, null));
        }
    }
}
