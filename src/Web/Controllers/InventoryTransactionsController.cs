using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Inventory.View")]
public class InventoryTransactionsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Inventory";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/inventory-transactions")]
public class InventoryTransactionsApiController : ControllerBase
{
    private readonly IInventoryTransactionService _inventoryTransactionService;

    public InventoryTransactionsApiController(IInventoryTransactionService inventoryTransactionService)
    {
        _inventoryTransactionService = inventoryTransactionService;
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

            var result = await _inventoryTransactionService.GetDataTableAsync(request, cancellationToken);
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
            var transaction = await _inventoryTransactionService.GetByIdAsync(id, cancellationToken);
            return transaction is null ? NotFound() : Ok(transaction);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("next-reference")]
    [RequirePermission("Inventory.Create")]
    public async Task<IActionResult> NextReference(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _inventoryTransactionService.GenerateNextReferenceAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("items")]
    [RequirePermission("Inventory.View")]
    public async Task<IActionResult> Items(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _inventoryTransactionService.GetItemLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("warehouses")]
    [RequirePermission("Inventory.View")]
    public async Task<IActionResult> Warehouses(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _inventoryTransactionService.GetWarehouseLookupsAsync(cancellationToken));
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
        [FromBody] InventoryTransactionSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new InventoryTransactionSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _inventoryTransactionService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new InventoryTransactionSaveResult(false, ex.Message, null));
        }
    }
}
