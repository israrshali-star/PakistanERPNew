using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Enums;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Settings.View")]
public class SystemJobsController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Settings";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/system-jobs")]
public class SystemJobsApiController : ControllerBase
{
    private readonly IDatabaseBackupService _databaseBackupService;
    private readonly IDataExportService _dataExportService;

    public SystemJobsApiController(
        IDatabaseBackupService databaseBackupService,
        IDataExportService dataExportService)
    {
        _databaseBackupService = databaseBackupService;
        _dataExportService = dataExportService;
    }

    [HttpGet("backups/datatable")]
    [RequirePermission("Settings.View")]
    public async Task<IActionResult> BackupsDataTable(CancellationToken cancellationToken)
    {
        var request = new DataTableRequest(
            Draw: int.TryParse(Request.Query["draw"], out var draw) ? draw : 0,
            Start: int.TryParse(Request.Query["start"], out var start) ? start : 0,
            Length: int.TryParse(Request.Query["length"], out var length) ? length : 10,
            SearchValue: Request.Query["search[value]"],
            OrderColumn: int.TryParse(Request.Query["order[0][column]"], out var col) ? col : 4,
            OrderDirection: Request.Query["order[0][dir]"].ToString());

        var result = await _databaseBackupService.GetDataTableAsync(request, cancellationToken);
        return Ok(new
        {
            draw = result.Draw,
            recordsTotal = result.RecordsTotal,
            recordsFiltered = result.RecordsFiltered,
            data = result.Data
        });
    }

    [HttpPost("backups/run")]
    [RequirePermission("Settings.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RunBackup(CancellationToken cancellationToken)
    {
        var result = await _databaseBackupService.RunBackupAsync(JobRunType.Manual, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("backups/download/{id:int}")]
    [RequirePermission("Settings.View")]
    public async Task<IActionResult> DownloadBackup(int id, CancellationToken cancellationToken)
    {
        var file = await _databaseBackupService.DownloadAsync(id, cancellationToken);
        if (file is null)
        {
            return NotFound(new { message = "Backup file not found." });
        }

        return File(file.Value.Content, "application/octet-stream", file.Value.FileName);
    }

    [HttpDelete("backups/delete/{id:int}")]
    [RequirePermission("Settings.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DeleteBackup(int id, CancellationToken cancellationToken)
    {
        var result = await _databaseBackupService.DeleteAsync(id, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("exports/datatable")]
    [RequirePermission("Settings.View")]
    public async Task<IActionResult> ExportsDataTable(CancellationToken cancellationToken)
    {
        try
        {
            var request = new DataTableRequest(
                Draw: int.TryParse(Request.Query["draw"], out var draw) ? draw : 0,
                Start: int.TryParse(Request.Query["start"], out var start) ? start : 0,
                Length: int.TryParse(Request.Query["length"], out var length) ? length : 10,
                SearchValue: Request.Query["search[value]"],
                OrderColumn: int.TryParse(Request.Query["order[0][column]"], out var col) ? col : 4,
                OrderDirection: Request.Query["order[0][dir]"].ToString());

            var result = await _dataExportService.GetDataTableAsync(request, cancellationToken);
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

    [HttpGet("exports/types")]
    [RequirePermission("Settings.View")]
    public async Task<IActionResult> ExportTypes(CancellationToken cancellationToken)
    {
        return Ok(await _dataExportService.GetExportTypesAsync(cancellationToken));
    }

    [HttpPost("exports/run/{type}")]
    [RequirePermission("Settings.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RunExport(string type, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<DataExportType>(type, true, out var exportType))
        {
            return BadRequest(new JobActionResult(false, "Invalid export type."));
        }

        try
        {
            var result = await _dataExportService.RunExportAsync(exportType, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new JobActionResult(false, ex.Message));
        }
    }

    [HttpGet("exports/download/{id:int}")]
    [RequirePermission("Settings.View")]
    public async Task<IActionResult> DownloadExport(int id, CancellationToken cancellationToken)
    {
        try
        {
            var file = await _dataExportService.DownloadAsync(id, cancellationToken);
            if (file is null)
            {
                return NotFound(new { message = "Export file not found." });
            }

            return File(
                file.Value.Content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                file.Value.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("exports/delete/{id:int}")]
    [RequirePermission("Settings.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DeleteExport(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _dataExportService.DeleteAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new JobActionResult(false, ex.Message));
        }
    }
}
