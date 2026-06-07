using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Sales.View")]
public class SalesInvoicesController : Controller
{
    private readonly ISalesInvoiceService _salesInvoiceService;

    public SalesInvoicesController(ISalesInvoiceService salesInvoiceService)
    {
        _salesInvoiceService = salesInvoiceService;
    }

    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Sales";
        return View();
    }

    [RequirePermission("Sales.Create")]
    public IActionResult Create()
    {
        ViewData["BreadcrumbParent"] = "Sales";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        try
        {
            var invoice = await _salesInvoiceService.GetDetailAsync(id, cancellationToken);
            if (invoice is null)
            {
                return NotFound();
            }

            ViewData["BreadcrumbParent"] = "Sales";
            ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
            return View(invoice);
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
[Route("api/sales-invoices")]
public class SalesInvoicesApiController : ControllerBase
{
    private readonly ISalesInvoiceService _salesInvoiceService;
    private readonly IStackLotInventoryService _stackLotInventory;
    private readonly ISalesInvoicePdfService _salesInvoicePdfService;

    public SalesInvoicesApiController(
        ISalesInvoiceService salesInvoiceService,
        IStackLotInventoryService stackLotInventory,
        ISalesInvoicePdfService salesInvoicePdfService)
    {
        _salesInvoiceService = salesInvoiceService;
        _stackLotInventory = stackLotInventory;
        _salesInvoicePdfService = salesInvoicePdfService;
    }

    [HttpGet("datatable")]
    [RequirePermission("Sales.View")]
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

            var result = await _salesInvoiceService.GetDataTableAsync(request, cancellationToken);
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

    [HttpGet("next-invoice-number")]
    [RequirePermission("Sales.Create")]
    public async Task<IActionResult> NextInvoiceNumber(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _salesInvoiceService.GenerateNextInvoiceNumberAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("customers")]
    [RequirePermission("Sales.Create")]
    public async Task<IActionResult> Customers(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _salesInvoiceService.GetCustomerLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("stack-availability")]
    [RequirePermission("Sales.Create")]
    public async Task<IActionResult> StackAvailability(
        [FromQuery] int itemId,
        [FromQuery] string? stackNo,
        [FromQuery] string? lotNo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (itemId <= 0)
            {
                return BadRequest(new { message = "Item is required." });
            }

            var availability = await _stackLotInventory.GetAvailabilityAsync(
                itemId,
                stackNo,
                lotNo,
                excludeInvoiceId: null,
                cancellationToken);

            return availability is null ? NotFound() : Ok(availability);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("items")]
    [RequirePermission("Sales.Create")]
    public async Task<IActionResult> Items(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _salesInvoiceService.GetItemLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("tax-rates")]
    [RequirePermission("Sales.Create")]
    public async Task<IActionResult> TaxRates(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _salesInvoiceService.GetTaxRatesAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}")]
    [RequirePermission("Sales.View")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        try
        {
            var invoice = await _salesInvoiceService.GetDetailAsync(id, cancellationToken);
            return invoice is null ? NotFound() : Ok(invoice);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/post")]
    [RequirePermission("Sales.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Post(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesInvoiceService.PostAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new SalesInvoiceActionResult(false, ex.Message, null));
        }
    }

    [HttpPost("{id:int}/cancel")]
    [RequirePermission("Sales.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesInvoiceService.CancelAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new SalesInvoiceActionResult(false, ex.Message, null));
        }
    }

    [HttpGet("{id:int}/fbr-payload")]
    [RequirePermission("Sales.Edit")]
    public async Task<IActionResult> FbrPayload(int id, CancellationToken cancellationToken)
    {
        try
        {
            var preview = await _salesInvoiceService.GetFbrPayloadPreviewAsync(id, cancellationToken);
            if (preview is null)
            {
                return BadRequest(new { message = "FBR payload is not available for this invoice." });
            }

            return Ok(preview);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/pdf")]
    [RequirePermission("Sales.View")]
    public async Task<IActionResult> Pdf(int id, CancellationToken cancellationToken)
    {
        try
        {
            var printData = await _salesInvoiceService.GetPrintDataAsync(id, cancellationToken);
            if (printData is null)
            {
                return BadRequest(new { message = "PDF is available only after FBR submission." });
            }

            var pdfBytes = _salesInvoicePdfService.GeneratePdf(printData);
            var fileName = $"{printData.InvoiceNumber}.pdf".Replace('/', '-');
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/submit-fbr")]
    [RequirePermission("Sales.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SubmitFbr(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesInvoiceService.SubmitToFbrAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new SalesInvoiceActionResult(false, ex.Message, null));
        }
    }

    [HttpPost]
    [RequirePermission("Sales.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] SalesInvoiceSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new SalesInvoiceSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _salesInvoiceService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new SalesInvoiceSaveResult(false, ex.Message, null));
        }
    }
}
