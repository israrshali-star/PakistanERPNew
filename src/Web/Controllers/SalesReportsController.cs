using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Reports.View")]
public class SalesReportsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Reports";
        return View();
    }

    public IActionResult SalesRegister()
    {
        ViewData["BreadcrumbParent"] = "Sales Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public IActionResult SalesByCustomer()
    {
        ViewData["BreadcrumbParent"] = "Sales Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public IActionResult SalesTaxSummary()
    {
        ViewData["BreadcrumbParent"] = "Sales Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/sales-reports")]
public class SalesReportsApiController : ControllerBase
{
    private readonly ISalesReportService _salesReportService;

    public SalesReportsApiController(ISalesReportService salesReportService)
    {
        _salesReportService = salesReportService;
    }

    [HttpGet("register")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> Register(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] int? customerId,
        [FromQuery] bool postedOnly = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _salesReportService.GetSalesRegisterAsync(
                new SalesReportRequest
                {
                    FromDate = fromDate,
                    ToDate = toDate,
                    CustomerId = customerId,
                    PostedOnly = postedOnly
                },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("by-customer")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> ByCustomer(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] int? customerId,
        [FromQuery] bool postedOnly = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _salesReportService.GetSalesByCustomerAsync(
                new SalesReportRequest
                {
                    FromDate = fromDate,
                    ToDate = toDate,
                    CustomerId = customerId,
                    PostedOnly = postedOnly
                },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("tax-summary")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> TaxSummary(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] int? customerId,
        [FromQuery] bool postedOnly = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _salesReportService.GetSalesTaxSummaryAsync(
                new SalesReportRequest
                {
                    FromDate = fromDate,
                    ToDate = toDate,
                    CustomerId = customerId,
                    PostedOnly = postedOnly
                },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("customers")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> Customers(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _salesReportService.GetCustomerLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
