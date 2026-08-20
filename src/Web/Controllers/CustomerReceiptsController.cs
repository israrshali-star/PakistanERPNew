using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Sales.View")]
public class CustomerReceiptsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Sales";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/customer-receipts")]
public class CustomerReceiptsApiController : ControllerBase
{
    private const long MaxAttachmentUploadBytes = 10 * 1024 * 1024;

    private readonly ICustomerReceiptService _customerReceiptService;
    private readonly ICustomerReceiptShareService _customerReceiptShareService;
    private readonly ICustomerReceiptAttachmentService _attachmentService;
    private readonly ICustomerReceiptInvoiceAllocationService _allocationService;

    public CustomerReceiptsApiController(
        ICustomerReceiptService customerReceiptService,
        ICustomerReceiptShareService customerReceiptShareService,
        ICustomerReceiptAttachmentService attachmentService,
        ICustomerReceiptInvoiceAllocationService allocationService)
    {
        _customerReceiptService = customerReceiptService;
        _customerReceiptShareService = customerReceiptShareService;
        _attachmentService = attachmentService;
        _allocationService = allocationService;
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

            DateTime? fromDate = DateTime.TryParse(Request.Query["fromDate"], out var from) ? from.Date : null;
            DateTime? toDate = DateTime.TryParse(Request.Query["toDate"], out var to) ? to.Date : null;

            var result = await _customerReceiptService.GetDataTableAsync(
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
    [RequirePermission("Sales.View")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await _customerReceiptService.GetByIdAsync(id, cancellationToken);
            return receipt is null ? NotFound() : Ok(receipt);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("next-receipt-number")]
    [RequirePermission("Sales.Create")]
    public async Task<IActionResult> NextReceiptNumber(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _customerReceiptService.GenerateNextReceiptNumberAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("customers")]
    [RequirePermission("Sales.View")]
    public async Task<IActionResult> Customers(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _customerReceiptService.GetCustomerLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("banks")]
    [RequirePermission("Sales.View")]
    public async Task<IActionResult> Banks(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _customerReceiptService.GetBankLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("invoice-allocation")]
    [RequirePermission("Sales.View")]
    public async Task<IActionResult> InvoiceAllocation(
        [FromQuery] int customerId,
        [FromQuery] DateTime receiptDate,
        [FromQuery] decimal amount,
        [FromQuery] int? receiptId,
        CancellationToken cancellationToken)
    {
        try
        {
            var allocation = await _allocationService.GetAllocationAsync(
                customerId,
                receiptDate,
                amount,
                receiptId,
                cancellationToken);
            return allocation is null ? NotFound() : Ok(allocation);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission("Sales.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] CustomerReceiptSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new CustomerReceiptSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _customerReceiptService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new CustomerReceiptSaveResult(false, ex.Message, null));
        }
    }

    [HttpPut("{id:int}")]
    [RequirePermission("Sales.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] CustomerReceiptSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new CustomerReceiptSaveResult(false, "Invalid request body.", null));
        }

        request.Id = id;

        try
        {
            var result = await _customerReceiptService.UpdateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new CustomerReceiptSaveResult(false, ex.Message, null));
        }
    }

    [HttpPost("{id:int}/approve-clearance")]
    [RequirePermission("Sales.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ApproveClearance(
        int id,
        [FromBody] CustomerReceiptApproveClearanceRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _customerReceiptService.ApproveClearanceAsync(id, request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new CustomerReceiptSaveResult(false, ex.Message, null));
        }
        catch (Exception ex)
        {
            return BadRequest(new CustomerReceiptSaveResult(false, ex.Message, null));
        }
    }

    [HttpPost("{id:int}/mark-returned")]
    [RequirePermission("Sales.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> MarkChequeReturned(
        int id,
        [FromBody] CustomerReceiptMarkReturnedRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _customerReceiptService.MarkChequeReturnedAsync(id, request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new CustomerReceiptSaveResult(false, ex.Message, null));
        }
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("Sales.Delete")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _customerReceiptService.DeleteAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new CustomerReceiptSaveResult(false, ex.Message, null));
        }
    }

    [HttpGet("{id:int}/share-info")]
    [RequirePermission("Sales.View")]
    public async Task<IActionResult> ShareInfo(int id, CancellationToken cancellationToken)
    {
        try
        {
            var info = await _customerReceiptShareService.GetShareInfoAsync(id, cancellationToken);
            return info is null ? NotFound() : Ok(info);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/pdf")]
    [RequirePermission("Sales.View")]
    public async Task<IActionResult> Pdf(
        int id,
        [FromQuery] bool urdu = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pdf = await _customerReceiptShareService.GetReceiptPdfAsync(id, urdu, cancellationToken);
            if (pdf is null)
            {
                return NotFound();
            }

            var suffix = urdu ? "-ur" : string.Empty;
            return File(pdf, "application/pdf", $"customer-receipt-{id}{suffix}.pdf");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/attachments")]
    [RequirePermission("Sales.View")]
    public async Task<IActionResult> Attachments(int id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _attachmentService.GetByReceiptIdAsync(id, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/attachments")]
    [RequirePermission("Sales.Create")]
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
    [RequirePermission("Sales.View")]
    public async Task<IActionResult> DownloadAttachment(
        int attachmentId,
        [FromQuery] bool download = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var file = await _attachmentService.DownloadAsync(attachmentId, cancellationToken);
            if (file is null)
            {
                return NotFound();
            }

            if (download)
            {
                return File(file.Content, file.ContentType, file.FileName);
            }

            // Inline view in browser (images/PDF) when reconciling on ledger.
            Response.Headers.ContentDisposition = $"inline; filename=\"{file.FileName}\"";
            return File(file.Content, file.ContentType);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("attachments/{attachmentId:int}")]
    [RequirePermission("Sales.Edit")]
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
