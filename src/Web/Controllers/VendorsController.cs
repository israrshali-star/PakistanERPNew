using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Vendors.View")]
public class VendorsController : Controller
{
    private readonly IVendorService _vendorService;

    public VendorsController(IVendorService vendorService)
    {
        _vendorService = vendorService;
    }

    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Purchase";
        return View();
    }

    public async Task<IActionResult> Ledger(int id, CancellationToken cancellationToken)
    {
        var ledger = await _vendorService.GetLedgerAsync(id, cancellationToken);
        if (ledger is null)
        {
            return NotFound();
        }

        ViewData["BreadcrumbParent"] = "Vendors";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View(ledger);
    }

    public async Task<IActionResult> Statement(int id, CancellationToken cancellationToken)
    {
        var vendor = await _vendorService.GetByIdAsync(id, cancellationToken);
        if (vendor is null)
        {
            return NotFound();
        }

        ViewData["BreadcrumbParent"] = "Vendors";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        ViewBag.Vendor = vendor;
        return View();
    }
}

[Authorize]
[Route("api/vendors")]
public class VendorsApiController : Controller
{
    private readonly IVendorService _vendorService;

    public VendorsApiController(IVendorService vendorService)
    {
        _vendorService = vendorService;
    }

    [HttpGet("datatable")]
    [RequirePermission("Vendors.View")]
    public async Task<IActionResult> DataTable(CancellationToken cancellationToken)
    {
        var request = new DataTableRequest(
            Draw: int.TryParse(Request.Query["draw"], out var draw) ? draw : 0,
            Start: int.TryParse(Request.Query["start"], out var start) ? start : 0,
            Length: int.TryParse(Request.Query["length"], out var length) ? length : 10,
            SearchValue: Request.Query["search[value]"],
            OrderColumn: int.TryParse(Request.Query["order[0][column]"], out var col) ? col : 1,
            OrderDirection: Request.Query["order[0][dir]"].ToString());

        var result = await _vendorService.GetDataTableAsync(request, cancellationToken);
        return Ok(new
        {
            draw = result.Draw,
            recordsTotal = result.RecordsTotal,
            recordsFiltered = result.RecordsFiltered,
            data = result.Data
        });
    }

    [HttpGet("{id:int}")]
    [RequirePermission("Vendors.View")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var vendor = await _vendorService.GetByIdAsync(id, cancellationToken);
        return vendor is null ? NotFound() : Ok(vendor);
    }

    [HttpGet("next-vendor-code")]
    [RequirePermission("Vendors.Create")]
    public async Task<IActionResult> NextVendorCode(CancellationToken cancellationToken)
    {
        return Ok(await _vendorService.GenerateNextVendorCodeAsync(cancellationToken));
    }

    [HttpPost]
    [RequirePermission("Vendors.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] VendorSaveRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _vendorService.CreateAsync(request, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{id:int}")]
    [RequirePermission("Vendors.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] VendorSaveRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request with { Id = id };
        var result = await _vendorService.UpdateAsync(payload, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("Vendors.Delete")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await _vendorService.DeleteAsync(id, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("{id:int}/ledger")]
    [RequirePermission("Vendors.View")]
    public async Task<IActionResult> Ledger(int id, CancellationToken cancellationToken)
    {
        var ledger = await _vendorService.GetLedgerAsync(id, cancellationToken);
        return ledger is null ? NotFound() : Ok(ledger);
    }

    [HttpGet("{id:int}/statement")]
    [RequirePermission("Vendors.View")]
    public async Task<IActionResult> Statement(
        int id,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var fromDate = from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var toDate = to ?? DateTime.Today;

        if (toDate < fromDate)
        {
            return BadRequest(new { message = "End date must be on or after start date." });
        }

        var statement = await _vendorService.GetStatementAsync(id, fromDate, toDate, cancellationToken);
        return statement is null ? NotFound() : Ok(statement);
    }
}
