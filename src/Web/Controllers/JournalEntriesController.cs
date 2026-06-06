using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("JournalEntries.View")]
public class JournalEntriesController : Controller
{
    private readonly IJournalEntryService _journalEntryService;

    public JournalEntriesController(IJournalEntryService journalEntryService)
    {
        _journalEntryService = journalEntryService;
    }

    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Accounting";
        return View();
    }

    [RequirePermission("JournalEntries.Create")]
    public IActionResult Create()
    {
        ViewData["BreadcrumbParent"] = "Journal Entries";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View();
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await _journalEntryService.GetDetailAsync(id, cancellationToken);
            if (entry is null)
            {
                return NotFound();
            }

            ViewData["BreadcrumbParent"] = "Journal Entries";
            ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
            return View(entry);
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
[Route("api/journal-entries")]
public class JournalEntriesApiController : ControllerBase
{
    private readonly IJournalEntryService _journalEntryService;

    public JournalEntriesApiController(IJournalEntryService journalEntryService)
    {
        _journalEntryService = journalEntryService;
    }

    [HttpGet("datatable")]
    [RequirePermission("JournalEntries.View")]
    public async Task<IActionResult> DataTable(CancellationToken cancellationToken)
    {
        try
        {
            var request = new DataTableRequest(
                Draw: int.TryParse(Request.Query["draw"], out var draw) ? draw : 0,
                Start: int.TryParse(Request.Query["start"], out var start) ? start : 0,
                Length: int.TryParse(Request.Query["length"], out var length) ? length : 10,
                SearchValue: Request.Query["search[value]"],
                OrderColumn: int.TryParse(Request.Query["order[0][column]"], out var col) ? col : 1,
                OrderDirection: Request.Query["order[0][dir]"].ToString());

            var result = await _journalEntryService.GetDataTableAsync(request, cancellationToken);
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
    [RequirePermission("JournalEntries.View")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await _journalEntryService.GetDetailAsync(id, cancellationToken);
            return entry is null ? NotFound() : Ok(entry);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("next-entry-number")]
    [RequirePermission("JournalEntries.Create")]
    public async Task<IActionResult> NextEntryNumber(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _journalEntryService.GenerateNextEntryNumberAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("accounts")]
    [RequirePermission("JournalEntries.Create")]
    public async Task<IActionResult> Accounts(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _journalEntryService.GetAccountLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission("JournalEntries.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] JournalEntrySaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new JournalEntrySaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _journalEntryService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new JournalEntrySaveResult(false, ex.Message, null));
        }
    }

    [HttpPost("{id:int}/post")]
    [RequirePermission("JournalEntries.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Post(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _journalEntryService.PostAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new JournalEntryActionResult(false, ex.Message, null));
        }
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("JournalEntries.Delete")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _journalEntryService.DeleteAsync(id, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new JournalEntryActionResult(false, ex.Message, null));
        }
    }
}
