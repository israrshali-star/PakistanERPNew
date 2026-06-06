using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Items.View")]
public class ItemsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Inventory";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/items")]
public class ItemsApiController : ControllerBase
{
    private readonly IItemService _itemService;

    public ItemsApiController(IItemService itemService)
    {
        _itemService = itemService;
    }

    [HttpGet("datatable")]
    [RequirePermission("Items.View")]
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

            var result = await _itemService.GetDataTableAsync(request, cancellationToken);
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
    [RequirePermission("Items.View")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        try
        {
            var item = await _itemService.GetByIdAsync(id, cancellationToken);
            return item is null ? NotFound() : Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("next-item-code")]
    [RequirePermission("Items.Create")]
    public async Task<IActionResult> NextItemCode(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _itemService.GenerateNextItemCodeAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("categories")]
    [RequirePermission("Items.View")]
    public async Task<IActionResult> Categories(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _itemService.GetCategoryLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission("Items.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] ItemSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new ItemSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _itemService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new ItemSaveResult(false, ex.Message, null));
        }
    }

    [HttpPut("{id:int}")]
    [RequirePermission("Items.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] ItemSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new ItemSaveResult(false, "Invalid request body.", null));
        }

        request.Id = id;

        try
        {
            var result = await _itemService.UpdateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new ItemSaveResult(false, ex.Message, null));
        }
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("Items.Delete")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _itemService.DeleteAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ItemSaveResult(false, ex.Message, null));
        }
    }
}
