using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Reports.View")]
public class PurchaseReportsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Reports";
        return View();
    }

    public IActionResult PurchaseRegister()
    {
        ViewData["BreadcrumbParent"] = "Purchase Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public IActionResult PurchaseByVendor()
    {
        ViewData["BreadcrumbParent"] = "Purchase Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public IActionResult InputTaxSummary()
    {
        ViewData["BreadcrumbParent"] = "Purchase Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/purchase-reports")]
public class PurchaseReportsApiController : ControllerBase
{
    private readonly IPurchaseReportService _purchaseReportService;

    public PurchaseReportsApiController(IPurchaseReportService purchaseReportService)
    {
        _purchaseReportService = purchaseReportService;
    }

    [HttpGet("register")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> Register(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] int? vendorId,
        [FromQuery] bool approvedOnly = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _purchaseReportService.GetPurchaseRegisterAsync(
                new PurchaseReportRequest
                {
                    FromDate = fromDate,
                    ToDate = toDate,
                    VendorId = vendorId,
                    ApprovedOnly = approvedOnly
                },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("by-vendor")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> ByVendor(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] int? vendorId,
        [FromQuery] bool approvedOnly = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _purchaseReportService.GetPurchaseByVendorAsync(
                new PurchaseReportRequest
                {
                    FromDate = fromDate,
                    ToDate = toDate,
                    VendorId = vendorId,
                    ApprovedOnly = approvedOnly
                },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("input-tax-summary")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> InputTaxSummary(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] int? vendorId,
        [FromQuery] bool approvedOnly = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _purchaseReportService.GetInputTaxSummaryAsync(
                new PurchaseReportRequest
                {
                    FromDate = fromDate,
                    ToDate = toDate,
                    VendorId = vendorId,
                    ApprovedOnly = approvedOnly
                },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("vendors")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> Vendors(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _purchaseReportService.GetVendorLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
