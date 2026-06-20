using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Enums;
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

    [RequirePermission("Purchase.Edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        try
        {
            var bill = await _vendorBillService.GetDetailAsync(id, cancellationToken);
            if (bill is null)
            {
                return NotFound();
            }

            if (bill.Status != BillStatus.Draft)
            {
                TempData["Error"] = "Only draft bills can be edited.";
                return RedirectToAction(nameof(Details), new { id });
            }

            ViewData["BreadcrumbParent"] = "Purchase";
            ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
            ViewData["EditBillId"] = id;
            return View("Create", bill);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
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
    private const long MaxAttachmentUploadBytes = 10 * 1024 * 1024;

    private readonly IVendorBillService _vendorBillService;
    private readonly IVendorBillAttachmentService _attachmentService;

    public VendorBillsApiController(
        IVendorBillService vendorBillService,
        IVendorBillAttachmentService attachmentService)
    {
        _vendorBillService = vendorBillService;
        _attachmentService = attachmentService;
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

            var result = await _vendorBillService.GetDataTableAsync(
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
    [RequirePermission("Purchase.View")]
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
    [RequirePermission("Purchase.View")]
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

    [HttpGet("warehouses")]
    [RequirePermission("Purchase.View")]
    public async Task<IActionResult> Warehouses(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _vendorBillService.GetWarehouseLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("purchase-tax-settings")]
    [RequirePermission("Purchase.View")]
    public async Task<IActionResult> PurchaseTaxSettings(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _vendorBillService.GetPurchaseTaxSettingsAsync(cancellationToken));
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

    [HttpPut("{id:int}")]
    [RequirePermission("Purchase.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] VendorBillSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new VendorBillSaveResult(false, "Invalid request body.", null));
        }

        request.Id = id;

        try
        {
            var result = await _vendorBillService.UpdateAsync(request, cancellationToken);
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

    [HttpPost("{id:int}/revert-to-draft")]
    [RequirePermission("Purchase.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RevertToDraft(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _vendorBillService.RevertToDraftAsync(id, cancellationToken);
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

    [HttpGet("{id:int}/attachments")]
    [RequirePermission("Purchase.View")]
    public async Task<IActionResult> Attachments(int id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _attachmentService.GetByBillIdAsync(id, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/attachments")]
    [RequirePermission("Purchase.Create")]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(MaxAttachmentUploadBytes)]
    public async Task<IActionResult> UploadAttachment(
        int id,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new DocumentAttachmentSaveResult(false, "Please select a file to upload.", null));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _attachmentService.UploadAsync(
                id,
                file.FileName,
                file.ContentType,
                stream,
                file.Length,
                cancellationToken);

            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new DocumentAttachmentSaveResult(false, ex.Message, null));
        }
    }

    [HttpGet("attachments/{attachmentId:int}/download")]
    [RequirePermission("Purchase.View")]
    public async Task<IActionResult> DownloadAttachment(int attachmentId, CancellationToken cancellationToken)
    {
        try
        {
            var file = await _attachmentService.DownloadAsync(attachmentId, cancellationToken);
            if (file is null)
            {
                return NotFound();
            }

            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("attachments/{attachmentId:int}")]
    [RequirePermission("Purchase.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DeleteAttachment(int attachmentId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _attachmentService.DeleteAsync(attachmentId, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new DocumentAttachmentSaveResult(false, ex.Message, null));
        }
    }
}
