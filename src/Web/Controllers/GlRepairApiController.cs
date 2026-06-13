using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/gl-repair")]
public class GlRepairApiController : ControllerBase
{
    private readonly IGlRepairService _glRepairService;

    public GlRepairApiController(IGlRepairService glRepairService)
    {
        _glRepairService = glRepairService;
    }

    [HttpPost("historical")]
    [RequirePermission("Settings.Edit")]
    public async Task<IActionResult> RepairHistorical(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _glRepairService.RepairHistoricalEntriesAsync(cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("cutover-reconcile")]
    [RequirePermission("Settings.Edit")]
    public async Task<IActionResult> ReconcileCutover(
        [FromQuery] int companyId,
        [FromQuery] DateTime? cutoverDate,
        CancellationToken cancellationToken)
    {
        try
        {
            var effectiveCutover = (cutoverDate ?? new DateTime(2026, 6, 1)).Date;
            var result = await _glRepairService.ReconcileToOpeningBalancesAsync(
                companyId,
                effectiveCutover,
                cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("post-cutover-transactions")]
    [RequirePermission("Settings.Edit")]
    public async Task<IActionResult> PostCutoverTransactions(
        [FromQuery] int companyId,
        [FromQuery] DateTime? fromDate,
        CancellationToken cancellationToken)
    {
        try
        {
            var effectiveFromDate = (fromDate ?? new DateTime(2026, 6, 1)).Date;
            var result = await _glRepairService.PostCutoverTransactionsAsync(
                companyId,
                effectiveFromDate,
                cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
