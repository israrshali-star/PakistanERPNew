using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Inventory.View")]
public class WarehousesController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Inventory";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/warehouses")]
public class WarehousesApiController : ControllerBase
{
    private readonly IWarehouseService _warehouseService;

    public WarehousesApiController(IWarehouseService warehouseService)
    {
        _warehouseService = warehouseService;
    }

    [HttpGet("datatable")]
    [RequirePermission("Inventory.View")]
    public async Task<IActionResult> DataTable(CancellationToken cancellationToken)
    {
        try
        {
            var request = new DataTableRequest(
                Draw: int.TryParse(Request.Query["draw"], out var draw) ? draw : 0,
                Start: int.TryParse(Request.Query["start"], out var start) ? start : 0,
                Length: int.TryParse(Request.Query["length"], out var length) ? length : 10,
                SearchValue: Request.Query["search[value]"],
                OrderColumn: int.TryParse(Request.Query["order[0][column]"], out var col) ? col : 1,
                OrderDirection: Request.Query["order[0][dir]"].ToString());

            var result = await _warehouseService.GetDataTableAsync(request, cancellationToken);
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
    [RequirePermission("Inventory.View")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        try
        {
            var warehouse = await _warehouseService.GetByIdAsync(id, cancellationToken);
            return warehouse is null ? NotFound() : Ok(warehouse);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("next-code")]
    [RequirePermission("Inventory.Create")]
    public async Task<IActionResult> NextCode(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _warehouseService.GenerateNextCodeAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission("Inventory.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] WarehouseSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new WarehouseSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _warehouseService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new WarehouseSaveResult(false, ex.Message, null));
        }
    }

    [HttpPut("{id:int}")]
    [RequirePermission("Inventory.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] WarehouseSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new WarehouseSaveResult(false, "Invalid request body.", null));
        }

        request.Id = id;

        try
        {
            var result = await _warehouseService.UpdateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new WarehouseSaveResult(false, ex.Message, null));
        }
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("Inventory.Delete")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _warehouseService.DeleteAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new WarehouseSaveResult(false, ex.Message, null));
        }
    }
}
