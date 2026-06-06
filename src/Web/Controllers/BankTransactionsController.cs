using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Banking.View")]
public class BankTransactionsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Banking";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/bank-transactions")]
public class BankTransactionsApiController : ControllerBase
{
    private readonly IBankTransactionService _bankTransactionService;

    public BankTransactionsApiController(IBankTransactionService bankTransactionService)
    {
        _bankTransactionService = bankTransactionService;
    }

    [HttpGet("datatable")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> DataTable(CancellationToken cancellationToken)
    {
        try
        {
            int? bankId = int.TryParse(Request.Query["bankId"], out var bid) ? bid : null;

            var request = new DataTableRequest(
                Draw: int.TryParse(Request.Query["draw"], out var draw) ? draw : 0,
                Start: int.TryParse(Request.Query["start"], out var start) ? start : 0,
                Length: int.TryParse(Request.Query["length"], out var length) ? length : 10,
                SearchValue: Request.Query["search[value]"],
                OrderColumn: int.TryParse(Request.Query["order[0][column]"], out var col) ? col : 1,
                OrderDirection: Request.Query["order[0][dir]"].ToString());

            var result = await _bankTransactionService.GetDataTableAsync(request, bankId, cancellationToken);
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

    [HttpGet("banks")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> Banks(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _bankTransactionService.GetBankLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission("Banking.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] BankTransactionSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new BankTransactionSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _bankTransactionService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new BankTransactionSaveResult(false, ex.Message, null));
        }
    }
}
