using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Purchase.View")]
public class VendorPaymentsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Purchase";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/vendor-payments")]
public class VendorPaymentsApiController : ControllerBase
{
    private readonly IVendorPaymentService _vendorPaymentService;
    private readonly IVendorPaymentShareService _vendorPaymentShareService;

    public VendorPaymentsApiController(
        IVendorPaymentService vendorPaymentService,
        IVendorPaymentShareService vendorPaymentShareService)
    {
        _vendorPaymentService = vendorPaymentService;
        _vendorPaymentShareService = vendorPaymentShareService;
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

            DateTime? fromDate = DateTime.TryParse(Request.Query["fromDate"], out var from) ? from.Date : null;
            DateTime? toDate = DateTime.TryParse(Request.Query["toDate"], out var to) ? to.Date : null;

            var result = await _vendorPaymentService.GetDataTableAsync(
                request,
                fromDate,
                toDate,
                cancellationToken);
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
            var payment = await _vendorPaymentService.GetByIdAsync(id, cancellationToken);
            return payment is null ? NotFound() : Ok(payment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("next-payment-number")]
    [RequirePermission("Purchase.Create")]
    public async Task<IActionResult> NextPaymentNumber(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _vendorPaymentService.GenerateNextPaymentNumberAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("vendors")]
    [RequirePermission("Purchase.View")]
    public async Task<IActionResult> Vendors(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _vendorPaymentService.GetVendorLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("banks")]
    [RequirePermission("Purchase.View")]
    public async Task<IActionResult> Banks(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _vendorPaymentService.GetBankLookupsAsync(cancellationToken));
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
        [FromBody] VendorPaymentSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new VendorPaymentSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _vendorPaymentService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new VendorPaymentSaveResult(false, ex.Message, null));
        }
    }

    [HttpPut("{id:int}")]
    [RequirePermission("Purchase.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] VendorPaymentSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new VendorPaymentSaveResult(false, "Invalid request body.", null));
        }

        request.Id = id;

        try
        {
            var result = await _vendorPaymentService.UpdateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new VendorPaymentSaveResult(false, ex.Message, null));
        }
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("Purchase.Delete")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _vendorPaymentService.DeleteAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new VendorPaymentSaveResult(false, ex.Message, null));
        }
    }

    [HttpGet("{id:int}/share-info")]
    [RequirePermission("Purchase.View")]
    public async Task<IActionResult> ShareInfo(int id, CancellationToken cancellationToken)
    {
        try
        {
            var info = await _vendorPaymentShareService.GetShareInfoAsync(id, cancellationToken);
            return info is null ? NotFound() : Ok(info);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/pdf")]
    [RequirePermission("Purchase.View")]
    public async Task<IActionResult> Pdf(int id, CancellationToken cancellationToken)
    {
        try
        {
            var pdf = await _vendorPaymentShareService.GetPaymentPdfAsync(id, cancellationToken);
            if (pdf is null)
            {
                return NotFound();
            }

            return File(pdf, "application/pdf", $"vendor-payment-{id}.pdf");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
