using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Customers.View")]
public class CustomersController : Controller
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Sales";
        return View();
    }

    public async Task<IActionResult> Ledger(int id, CancellationToken cancellationToken)
    {
        try
        {
            var ledger = await _customerService.GetLedgerAsync(id, cancellationToken);
            if (ledger is null)
            {
                return NotFound();
            }

            ViewData["BreadcrumbParent"] = "Customers";
            ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
            return View(ledger);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    public async Task<IActionResult> Statement(int id, CancellationToken cancellationToken)
    {
        try
        {
            var customer = await _customerService.GetByIdAsync(id, cancellationToken);
            if (customer is null)
            {
                return NotFound();
            }

            ViewData["BreadcrumbParent"] = "Customers";
            ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
            ViewBag.Customer = customer;
            return View();
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
[Route("api/customers")]
public class CustomersApiController : ControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersApiController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpGet("datatable")]
    [RequirePermission("Customers.View")]
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

            var result = await _customerService.GetDataTableAsync(request, cancellationToken);
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
    [RequirePermission("Customers.View")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        try
        {
            var customer = await _customerService.GetByIdAsync(id, cancellationToken);
            return customer is null ? NotFound() : Ok(customer);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("next-buyer-id")]
    [RequirePermission("Customers.Create")]
    public async Task<IActionResult> NextBuyerId(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _customerService.GenerateNextBuyerIdAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission("Customers.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] CustomerSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new CustomerSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _customerService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new CustomerSaveResult(false, ex.Message, null));
        }
    }

    [HttpPut("{id:int}")]
    [RequirePermission("Customers.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] CustomerSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new CustomerSaveResult(false, "Invalid request body.", null));
        }

        request.Id = id;

        try
        {
            var result = await _customerService.UpdateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new CustomerSaveResult(false, ex.Message, null));
        }
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("Customers.Delete")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _customerService.DeleteAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new CustomerSaveResult(false, ex.Message, null));
        }
    }

    [HttpGet("{id:int}/ledger")]
    [RequirePermission("Customers.View")]
    public async Task<IActionResult> Ledger(int id, CancellationToken cancellationToken)
    {
        try
        {
            var ledger = await _customerService.GetLedgerAsync(id, cancellationToken);
            return ledger is null ? NotFound() : Ok(ledger);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/statement")]
    [RequirePermission("Customers.View")]
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

        try
        {
            var statement = await _customerService.GetStatementAsync(id, fromDate, toDate, cancellationToken);
            return statement is null ? NotFound() : Ok(statement);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
