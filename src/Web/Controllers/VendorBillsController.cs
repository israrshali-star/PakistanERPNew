using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Purchase.View")]
public class VendorBillsController : Controller
{
    private readonly IVendorBillService _vendorBillService;

    public VendorBillsController(IVendorBillService vendorBillService)
    {
        _vendorBillService = vendorBillService;
    }

    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Purchase";
        return View();
    }

    [RequirePermission("Purchase.Create")]
    public IActionResult Create()
    {
        ViewData["BreadcrumbParent"] = "Purchase";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        try
        {
            var bill = await _vendorBillService.GetDetailAsync(id, cancellationToken);
            if (bill is null)
            {
                return NotFound();
            }

            ViewData["BreadcrumbParent"] = "Purchase";
            ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
            return View(bill);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }
}

[Authorize]
[ApiController]
[Route("api/vendor-bills")]
public class VendorBillsApiController : ControllerBase
{
    private readonly IVendorBillService _vendorBillService;

    public VendorBillsApiController(IVendorBillService vendorBillService)
    {
        _vendorBillService = vendorBillService;
    }

    [HttpGet("datatable")]
    [RequirePermission("Purchase.View")]
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

            var result = await _vendorBillService.GetDataTableAsync(request, cancellationToken);
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
    [RequirePermission("Purchase.View")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        try
        {
            var bill = await _vendorBillService.GetDetailAsync(id, cancellationToken);
            return bill is null ? NotFound() : Ok(bill);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("next-bill-number")]
    [RequirePermission("Purchase.Create")]
    public async Task<IActionResult> NextBillNumber(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _vendorBillService.GenerateNextBillNumberAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("vendors")]
    [RequirePermission("Purchase.Create")]
    public async Task<IActionResult> Vendors(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _vendorBillService.GetVendorLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("items")]
    [RequirePermission("Purchase.Create")]
    public async Task<IActionResult> Items(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _vendorBillService.GetItemLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission("Purchase.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] VendorBillSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new VendorBillSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _vendorBillService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new VendorBillSaveResult(false, ex.Message, null));
        }
    }

    [HttpPost("{id:int}/approve")]
    [RequirePermission("Purchase.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _vendorBillService.ApproveAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new VendorBillActionResult(false, ex.Message, null));
        }
    }

    [HttpPost("{id:int}/cancel")]
    [RequirePermission("Purchase.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _vendorBillService.CancelAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new VendorBillActionResult(false, ex.Message, null));
        }
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("Purchase.Delete")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _vendorBillService.DeleteAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new VendorBillActionResult(false, ex.Message, null));
        }
    }
}
