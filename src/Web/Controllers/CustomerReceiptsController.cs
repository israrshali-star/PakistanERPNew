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
    private readonly ICustomerReceiptService _customerReceiptService;

    public CustomerReceiptsApiController(ICustomerReceiptService customerReceiptService)
    {
        _customerReceiptService = customerReceiptService;
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

            var result = await _customerReceiptService.GetDataTableAsync(request, cancellationToken);
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
}
