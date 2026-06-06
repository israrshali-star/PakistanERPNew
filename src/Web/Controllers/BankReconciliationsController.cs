using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Banking.View")]
public class BankReconciliationsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Banking";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/bank-reconciliations")]
public class BankReconciliationsApiController : ControllerBase
{
    private readonly IBankReconciliationService _bankReconciliationService;

    public BankReconciliationsApiController(IBankReconciliationService bankReconciliationService)
    {
        _bankReconciliationService = bankReconciliationService;
    }

    [HttpGet("datatable")]
    [RequirePermission("Banking.View")]
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

            var result = await _bankReconciliationService.GetDataTableAsync(request, cancellationToken);
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

    [HttpGet("preview/{bankId:int}")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> Preview(int bankId, CancellationToken cancellationToken)
    {
        try
        {
            var preview = await _bankReconciliationService.GetPreviewAsync(bankId, cancellationToken);
            return preview is null ? NotFound() : Ok(preview);
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
            return Ok(await _bankReconciliationService.GetBankLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("complete")]
    [RequirePermission("Banking.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Complete(
        [FromBody] BankReconciliationCompleteRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new BankReconciliationCompleteResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _bankReconciliationService.CompleteAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new BankReconciliationCompleteResult(false, ex.Message, null));
        }
    }
}
