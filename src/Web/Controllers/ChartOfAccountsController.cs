using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("ChartOfAccounts.View")]
public class ChartOfAccountsController : Controller
{
    private readonly IChartOfAccountsService _chartOfAccountsService;

    public ChartOfAccountsController(IChartOfAccountsService chartOfAccountsService)
    {
        _chartOfAccountsService = chartOfAccountsService;
    }

    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Accounting";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/chart-of-accounts")]
public class ChartOfAccountsApiController : ControllerBase
{
    private readonly IChartOfAccountsService _chartOfAccountsService;

    public ChartOfAccountsApiController(IChartOfAccountsService chartOfAccountsService)
    {
        _chartOfAccountsService = chartOfAccountsService;
    }

    [HttpGet("tree")]
    [RequirePermission("ChartOfAccounts.View")]
    public async Task<IActionResult> Tree(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _chartOfAccountsService.GetTreeAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("by-number")]
    [RequirePermission("ChartOfAccounts.View")]
    public async Task<IActionResult> GetByNumber(
        string accountNumber,
        CancellationToken cancellationToken)
    {
        var account = await _chartOfAccountsService.GetByAccountNumberAsync(accountNumber, cancellationToken);
        return account is null ? NotFound() : Ok(account);
    }

    [HttpGet("parents")]
    [RequirePermission("ChartOfAccounts.View")]
    public async Task<IActionResult> Parents(
        int typeId,
        int subTypeId,
        int? excludeAccountId = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _chartOfAccountsService.GetParentAccountsAsync(
            typeId,
            subTypeId,
            excludeAccountId,
            cancellationToken));
    }

    [HttpGet("suggest-number")]
    [RequirePermission("ChartOfAccounts.Create")]
    public async Task<IActionResult> SuggestNumber(
        int typeId,
        int subTypeId,
        int? parentAccountId = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _chartOfAccountsService.SuggestAccountNumberAsync(
            typeId,
            subTypeId,
            parentAccountId,
            cancellationToken));
    }

    [HttpGet("{id:int}")]
    [RequirePermission("ChartOfAccounts.View")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var account = await _chartOfAccountsService.GetByIdAsync(id, cancellationToken);
        return account is null ? NotFound() : Ok(account);
    }

    [HttpGet("{id:int}/ledger")]
    [RequirePermission("ChartOfAccounts.View")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Ledger(
        int id,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        try
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
            {
                return BadRequest(new { message = "From date cannot be after to date." });
            }

            var ledger = await _chartOfAccountsService.GetLedgerAsync(
                id,
                fromDate?.Date,
                toDate?.Date,
                cancellationToken);
            return ledger is null ? NotFound() : Ok(ledger);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/ledger/export")]
    [RequirePermission("ChartOfAccounts.View")]
    public async Task<IActionResult> LedgerExport(
        int id,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        try
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
            {
                return BadRequest(new { message = "From date cannot be after to date." });
            }

            var account = await _chartOfAccountsService.GetByIdAsync(id, cancellationToken);
            if (account is null || account.IsGroupAccount)
            {
                return NotFound();
            }

            var bytes = await _chartOfAccountsService.ExportLedgerToExcelAsync(
                id,
                fromDate?.Date,
                toDate?.Date,
                cancellationToken);
            if (bytes is null)
            {
                return NotFound();
            }

            var fileName = $"AccountLedger_{account.AccountNumber}_{DateTime.Now:yyyyMMdd}.xlsx";
            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/ledger/pdf")]
    [RequirePermission("ChartOfAccounts.View")]
    public async Task<IActionResult> LedgerPdf(
        int id,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        try
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
            {
                return BadRequest(new { message = "From date cannot be after to date." });
            }

            var account = await _chartOfAccountsService.GetByIdAsync(id, cancellationToken);
            if (account is null || account.IsGroupAccount)
            {
                return NotFound();
            }

            var bytes = await _chartOfAccountsService.ExportLedgerToPdfAsync(
                id,
                fromDate?.Date,
                toDate?.Date,
                cancellationToken);
            if (bytes is null)
            {
                return NotFound();
            }

            var fileName = $"AccountLedger_{account.AccountNumber}_{DateTime.Now:yyyyMMdd}.pdf";
            return File(bytes, "application/pdf", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission("ChartOfAccounts.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] ChartOfAccountSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new ChartOfAccountSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _chartOfAccountsService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new ChartOfAccountSaveResult(false, ex.Message, null));
        }
    }

    [HttpPut("{id:int}")]
    [RequirePermission("ChartOfAccounts.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] ChartOfAccountSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new ChartOfAccountSaveResult(false, "Invalid request body.", null));
        }

        request.Id = id;

        try
        {
            var result = await _chartOfAccountsService.UpdateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new ChartOfAccountSaveResult(false, ex.Message, null));
        }
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("ChartOfAccounts.Delete")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await _chartOfAccountsService.DeleteAsync(id, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("export")]
    [RequirePermission("ChartOfAccounts.View")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var bytes = await _chartOfAccountsService.ExportToExcelAsync(cancellationToken);
        var fileName = $"ChartOfAccounts_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
