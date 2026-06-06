using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Banking.View")]
public class BanksController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Banking";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/banks")]
public class BanksApiController : ControllerBase
{
    private readonly IBankService _bankService;

    public BanksApiController(IBankService bankService)
    {
        _bankService = bankService;
    }

    [HttpGet("datatable")]
    [RequirePermission("Banking.View")]
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

            var result = await _bankService.GetDataTableAsync(request, cancellationToken);
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

    [HttpGet("{id:int}")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        try
        {
            var bank = await _bankService.GetByIdAsync(id, cancellationToken);
            return bank is null ? NotFound() : Ok(bank);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("chart-of-accounts")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> ChartOfAccounts(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _bankService.GetChartOfAccountLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission("Banking.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create([FromBody] BankSaveRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new BankSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _bankService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new BankSaveResult(false, ex.Message, null));
        }
    }

    [HttpPut("{id:int}")]
    [RequirePermission("Banking.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(int id, [FromBody] BankSaveRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new BankSaveResult(false, "Invalid request body.", null));
        }

        request.Id = id;

        try
        {
            var result = await _bankService.UpdateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new BankSaveResult(false, ex.Message, null));
        }
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("Banking.Delete")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _bankService.DeleteAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new BankSaveResult(false, ex.Message, null));
        }
    }
}
