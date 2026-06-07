using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("AuditLogs.View")]
public class AuditLogsController : Controller
{
    private readonly IAuditLogService _auditLogService;

    public AuditLogsController(IAuditLogService auditLogService)
    {
        _auditLogService = auditLogService;
    }

    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Settings";
        return View();
    }

    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        try
        {
            var detail = await _auditLogService.GetByIdAsync(id, cancellationToken);
            if (detail is null)
            {
                return NotFound();
            }

            ViewData["BreadcrumbParent"] = "Audit Logs";
            ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
            return View(detail);
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
[Route("api/audit-logs")]
public class AuditLogsApiController : ControllerBase
{
    private readonly IAuditLogService _auditLogService;

    public AuditLogsApiController(IAuditLogService auditLogService)
    {
        _auditLogService = auditLogService;
    }

    [HttpGet("datatable")]
    [RequirePermission("AuditLogs.View")]
    public async Task<IActionResult> DataTable(CancellationToken cancellationToken)
    {
        try
        {
            var request = new DataTableRequest(
                Draw: int.TryParse(Request.Query["draw"], out var draw) ? draw : 0,
                Start: int.TryParse(Request.Query["start"], out var start) ? start : 0,
                Length: int.TryParse(Request.Query["length"], out var length) ? length : 10,
                SearchValue: Request.Query["search[value]"],
                OrderColumn: int.TryParse(Request.Query["order[0][column]"], out var col) ? col : 0,
                OrderDirection: Request.Query["order[0][dir]"].ToString());

            var result = await _auditLogService.GetDataTableAsync(request, cancellationToken);
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

    [HttpGet("{id:long}")]
    [RequirePermission("AuditLogs.View")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        try
        {
            var detail = await _auditLogService.GetByIdAsync(id, cancellationToken);
            return detail is null ? NotFound() : Ok(detail);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
