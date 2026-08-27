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

    public IActionResult PurchaseByRelatedVendor()
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

    public IActionResult StackLotTracking()
    {
        ViewData["BreadcrumbParent"] = "Purchase Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public IActionResult VendorPaymentsMonthly()
    {
        ViewData["BreadcrumbParent"] = "Purchase Reports";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public IActionResult MonthlyPurchaseByVendor()
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
    private readonly IVendorPaymentMonthlyPdfService _vendorPaymentMonthlyPdfService;
    private readonly ICompanyService _companyService;

    public PurchaseReportsApiController(
        IPurchaseReportService purchaseReportService,
        IVendorPaymentMonthlyPdfService vendorPaymentMonthlyPdfService,
        ICompanyService companyService)
    {
        _purchaseReportService = purchaseReportService;
        _vendorPaymentMonthlyPdfService = vendorPaymentMonthlyPdfService;
        _companyService = companyService;
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
        [FromQuery] bool relatedCompaniesOnly = false,
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
                    ApprovedOnly = approvedOnly,
                    RelatedCompaniesOnly = relatedCompaniesOnly
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

    [HttpGet("stack-lot-tracking")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> StackLotTracking(
        [FromQuery] int? itemId,
        [FromQuery] string? lotNo,
        [FromQuery] string? stackNo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _purchaseReportService.GetStackLotTrackingAsync(
                new StackLotTrackingRequest
                {
                    ItemId = itemId,
                    LotNo = lotNo,
                    StackNo = stackNo
                },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("stack-lot-items")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> StackLotItems(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _purchaseReportService.GetStackLotItemLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("stack-lot-filters")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> StackLotFilters(
        [FromQuery] int? itemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _purchaseReportService.GetStackLotFilterLookupsAsync(itemId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("monthly-by-vendor")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> MonthlyByVendor(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] int? vendorId,
        [FromQuery] bool approvedOnly = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _purchaseReportService.GetMonthlyPurchaseByVendorAsync(
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

    [HttpGet("vendor-payments-monthly")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> VendorPaymentsMonthly(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] int? vendorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _purchaseReportService.GetVendorPaymentMonthlyAsync(
                new PurchaseReportRequest
                {
                    FromDate = fromDate,
                    ToDate = toDate,
                    VendorId = vendorId
                },
                cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("vendor-payments-monthly/pdf")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> VendorPaymentsMonthlyPdf(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] int? vendorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _purchaseReportService.GetVendorPaymentMonthlyAsync(
                new PurchaseReportRequest
                {
                    FromDate = fromDate,
                    ToDate = toDate,
                    VendorId = vendorId
                },
                cancellationToken);
            var company = await _companyService.GetCurrentCompanyAsync(cancellationToken);
            var pdf = _vendorPaymentMonthlyPdfService.GeneratePdf(
                report,
                company?.CompanyName ?? "Pakistan Accounting ERP");
            var fileName = $"vendor-payments-monthly-{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.pdf";
            return File(pdf, "application/pdf", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

