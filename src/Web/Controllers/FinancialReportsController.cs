using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Reports.View")]
public class FinancialReportsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Reports";
        return View();
    }

    public IActionResult TrialBalance()
    {
        ViewData["BreadcrumbParent"] = "Financial Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public IActionResult ProfitAndLoss()
    {
        ViewData["BreadcrumbParent"] = "Financial Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public IActionResult BalanceSheet()
    {
        ViewData["BreadcrumbParent"] = "Financial Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public IActionResult ArAgingSummary()
    {
        ViewData["BreadcrumbParent"] = "Financial Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/financial-reports")]
public class FinancialReportsApiController : ControllerBase
{
    private readonly IFinancialReportService _financialReportService;

    public FinancialReportsApiController(IFinancialReportService financialReportService)
    {
        _financialReportService = financialReportService;
    }

    [HttpGet("trial-balance")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> TrialBalance(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _financialReportService.GetTrialBalanceAsync(
                new FinancialReportDateRangeRequest { FromDate = fromDate, ToDate = toDate },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("profit-and-loss")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> ProfitAndLoss(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _financialReportService.GetProfitAndLossAsync(
                new FinancialReportDateRangeRequest { FromDate = fromDate, ToDate = toDate },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("balance-sheet")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> BalanceSheet(
        [FromQuery] DateTime asOfDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _financialReportService.GetBalanceSheetAsync(
                new BalanceSheetReportRequest { AsOfDate = asOfDate },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("ar-aging-summary")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> ArAgingSummary(
        [FromQuery] DateTime asOfDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _financialReportService.GetArAgingSummaryAsync(
                new ArAgingReportRequest { AsOfDate = asOfDate },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
