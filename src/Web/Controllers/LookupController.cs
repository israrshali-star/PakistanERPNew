using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.Interfaces.Services;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[Route("api/[controller]")]
public class LookupController : Controller
{
    private readonly ILookupService _lookupService;
    private readonly IStackLotInventoryService _stackLotInventory;

    public LookupController(ILookupService lookupService, IStackLotInventoryService stackLotInventory)
    {
        _lookupService = lookupService;
        _stackLotInventory = stackLotInventory;
    }

    [HttpGet("amount-in-words")]
    public IActionResult AmountInWords([FromQuery] decimal amount)
    {
        return Ok(new { text = PakistanAccountingERP.Application.Common.AmountInWords.ToPakistaniRupees(amount) });
    }

    [HttpGet("account-types")]
    public async Task<IActionResult> AccountTypes(CancellationToken cancellationToken)
    {
        return Ok(await _lookupService.GetAccountTypesAsync(cancellationToken));
    }

    [HttpGet("sub-account-types")]
    public async Task<IActionResult> SubAccountTypes(int? typeId, CancellationToken cancellationToken)
    {
        return Ok(await _lookupService.GetSubAccountTypesAsync(typeId, cancellationToken));
    }

    [HttpGet("provinces")]
    public async Task<IActionResult> Provinces(CancellationToken cancellationToken)
    {
        return Ok(await _lookupService.GetProvincesAsync(cancellationToken));
    }

    [HttpGet("scenario-types")]
    public async Task<IActionResult> ScenarioTypes(CancellationToken cancellationToken)
    {
        return Ok(await _lookupService.GetScenarioTypesAsync(cancellationToken));
    }

    [HttpGet("units-of-measure")]
    public async Task<IActionResult> UnitsOfMeasure(CancellationToken cancellationToken)
    {
        return Ok(await _lookupService.GetUnitsOfMeasureAsync(cancellationToken));
    }

    [HttpGet("lot-numbers")]
    public async Task<IActionResult> LotNumbers(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _stackLotInventory.GetLotNumbersAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("lot-detail")]
    public async Task<IActionResult> LotDetail(
        [FromQuery] string lotNo,
        [FromQuery] string? itemCode,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(lotNo) && string.IsNullOrWhiteSpace(itemCode))
            {
                return BadRequest(new { message = "Item code or lot number is required." });
            }

            var detail = await _stackLotInventory.GetLotDetailAsync(lotNo ?? string.Empty, itemCode, cancellationToken);
            return detail is null ? NotFound(new { message = "No item found for this lot number." }) : Ok(detail);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("stack-availability")]
    public async Task<IActionResult> StackAvailability(
        [FromQuery] int itemId,
        [FromQuery] string? stackNo,
        [FromQuery] string? lotNo,
        CancellationToken cancellationToken)
    {
        try
        {
            if (itemId <= 0)
            {
                return BadRequest(new { message = "Item is required." });
            }

            var availability = await _stackLotInventory.GetAvailabilityAsync(
                itemId,
                stackNo,
                lotNo,
                excludeInvoiceId: null,
                cancellationToken);

            return availability is null ? NotFound() : Ok(availability);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
