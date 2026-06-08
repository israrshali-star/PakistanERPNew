using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Reports.View")]
public class CustomReportsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Reports";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/custom-reports")]
public class CustomReportsApiController : ControllerBase
{
    private readonly ICustomReportService _customReportService;

    public CustomReportsApiController(ICustomReportService customReportService)
    {
        _customReportService = customReportService;
    }

    [HttpGet("sources")]
    [RequirePermission("Reports.View")]
    public async Task<IActionResult> Sources(CancellationToken cancellationToken)
    {
        return Ok(await _customReportService.GetSourcesAsync(cancellationToken));
    }

    [HttpPost("run")]
    [RequirePermission("Reports.View")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Run(
        [FromBody] CustomReportRunRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _customReportService.RunAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("export")]
    [RequirePermission("Reports.View")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Export(
        [FromBody] CustomReportRunRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _customReportService.ExportToExcelAsync(request, cancellationToken);
            var fileName = $"CustomReport_{request.SourceKey}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
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
}
