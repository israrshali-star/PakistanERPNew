using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Users.View")]
public class UsersController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Settings";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/users")]
public class UsersApiController : ControllerBase
{
    private readonly IUserManagementService _userManagementService;

    public UsersApiController(IUserManagementService userManagementService)
    {
        _userManagementService = userManagementService;
    }

    [HttpGet("datatable")]
    [RequirePermission("Users.View")]
    public async Task<IActionResult> DataTable(CancellationToken cancellationToken)
    {
        var request = new DataTableRequest(
            Draw: int.TryParse(Request.Query["draw"], out var draw) ? draw : 0,
            Start: int.TryParse(Request.Query["start"], out var start) ? start : 0,
            Length: int.TryParse(Request.Query["length"], out var length) ? length : 10,
            SearchValue: Request.Query["search[value]"],
            OrderColumn: int.TryParse(Request.Query["order[0][column]"], out var col) ? col : 0,
            OrderDirection: Request.Query["order[0][dir]"].ToString());

        var result = await _userManagementService.GetDataTableAsync(request, cancellationToken);
        return Ok(new
        {
            draw = result.Draw,
            recordsTotal = result.RecordsTotal,
            recordsFiltered = result.RecordsFiltered,
            data = result.Data
        });
    }

    [HttpGet("{id}")]
    [RequirePermission("Users.View")]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var user = await _userManagementService.GetByIdAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpGet("lookups/companies")]
    [RequirePermission("Users.View")]
    public async Task<IActionResult> CompanyLookups(CancellationToken cancellationToken)
    {
        return Ok(await _userManagementService.GetCompanyLookupsAsync(cancellationToken));
    }

    [HttpGet("lookups/roles")]
    [RequirePermission("Users.View")]
    public async Task<IActionResult> RoleLookups(CancellationToken cancellationToken)
    {
        return Ok(await _userManagementService.GetRoleLookupsAsync(cancellationToken));
    }

    [HttpPost]
    [RequirePermission("Users.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] UserCreateRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new UserSaveResult(false, "Invalid request body.", null));
        }

        var result = await _userManagementService.CreateAsync(request, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{id}")]
    [RequirePermission("Users.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Update(
        string id,
        [FromBody] UserUpdateRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new UserSaveResult(false, "Invalid request body.", null));
        }

        var result = await _userManagementService.UpdateAsync(id, request, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id}/reset-password")]
    [RequirePermission("Users.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ResetPassword(
        string id,
        [FromBody] UserResetPasswordRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new UserActionResult(false, "New password is required."));
        }

        var result = await _userManagementService.ResetPasswordAsync(id, request.NewPassword, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("{id}")]
    [RequirePermission("Users.Delete")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var result = await _userManagementService.DeleteAsync(id, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
