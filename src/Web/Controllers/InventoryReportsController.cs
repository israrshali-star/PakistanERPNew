using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Inventory.View")]
public class InventoryReportsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Inventory";
        return View();
    }

    public IActionResult StockSummary()
    {
        ViewData["BreadcrumbParent"] = "Inventory Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public IActionResult StackWiseStock()
    {
        ViewData["BreadcrumbParent"] = "Inventory Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public IActionResult LowStock()
    {
        ViewData["BreadcrumbParent"] = "Inventory Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public IActionResult StockMovement()
    {
        ViewData["BreadcrumbParent"] = "Inventory Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/inventory-reports")]
public class InventoryReportsApiController : ControllerBase
{
    private readonly IInventoryReportService _inventoryReportService;

    public InventoryReportsApiController(IInventoryReportService inventoryReportService)
    {
        _inventoryReportService = inventoryReportService;
    }

    [HttpGet("stock-summary")]
    [RequirePermission("Inventory.View")]
    public async Task<IActionResult> StockSummary(
        [FromQuery] int? categoryId,
        [FromQuery] bool activeOnly = true,
        [FromQuery] bool hideZeroQoh = false,
        [FromQuery] DateTime? asOfDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _inventoryReportService.GetStockSummaryAsync(
                new StockSummaryReportRequest
                {
                    CategoryId = categoryId,
                    ActiveOnly = activeOnly,
                    HideZeroQoh = hideZeroQoh,
                    AsOfDate = asOfDate
                },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("stack-wise-stock")]
    [RequirePermission("Inventory.View")]
    public async Task<IActionResult> StackWiseStock(
        [FromQuery] int? categoryId,
        [FromQuery] bool activeOnly = true,
        [FromQuery] bool hideZeroQoh = false,
        [FromQuery] DateTime? asOfDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _inventoryReportService.GetStackWiseStockAsync(
                new StockSummaryReportRequest
                {
                    CategoryId = categoryId,
                    ActiveOnly = activeOnly,
                    HideZeroQoh = hideZeroQoh,
                    AsOfDate = asOfDate
                },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("low-stock")]
    [RequirePermission("Inventory.View")]
    public async Task<IActionResult> LowStock(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _inventoryReportService.GetLowStockReportAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("stock-movement")]
    [RequirePermission("Inventory.View")]
    public async Task<IActionResult> StockMovement(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] int? itemId,
        [FromQuery] int? warehouseId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _inventoryReportService.GetStockMovementReportAsync(
                new StockMovementReportRequest
                {
                    FromDate = fromDate,
                    ToDate = toDate,
                    ItemId = itemId,
                    WarehouseId = warehouseId
                },
                cancellationToken);
            return Ok(report);
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
            return Ok(await _inventoryReportService.GetItemLookupsAsync(cancellationToken));
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
            return Ok(await _inventoryReportService.GetWarehouseLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("categories")]
    [RequirePermission("Inventory.View")]
    public async Task<IActionResult> Categories(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _inventoryReportService.GetCategoryLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
