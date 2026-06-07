using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Users.View")]
public class RolesController : Controller
{
    private readonly IRolePermissionManagementService _rolePermissionService;

    public RolesController(IRolePermissionManagementService rolePermissionService)
    {
        _rolePermissionService = rolePermissionService;
    }

    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Settings";
        return View();
    }

    [RequirePermission("Users.Edit")]
    public async Task<IActionResult> Permissions(string id, CancellationToken cancellationToken)
    {
        var role = await _rolePermissionService.GetRolePermissionsAsync(id, cancellationToken);
        if (role is null)
        {
            return NotFound();
        }

        ViewData["BreadcrumbParent"] = "Roles & Permissions";
        ViewData["BreadcrumbParentUrl"] = Url.Action(nameof(Index));
        return View(role);
    }
}

[Authorize]
[ApiController]
[Route("api/roles")]
public class RolesApiController : ControllerBase
{
    private readonly IRolePermissionManagementService _rolePermissionService;

    public RolesApiController(IRolePermissionManagementService rolePermissionService)
    {
        _rolePermissionService = rolePermissionService;
    }

    [HttpGet]
    [RequirePermission("Users.View")]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        return Ok(await _rolePermissionService.GetRolesAsync(cancellationToken));
    }

    [HttpGet("{id}/permissions")]
    [RequirePermission("Users.View")]
    public async Task<IActionResult> Permissions(string id, CancellationToken cancellationToken)
    {
        var role = await _rolePermissionService.GetRolePermissionsAsync(id, cancellationToken);
        return role is null ? NotFound() : Ok(role);
    }

    [HttpPut("{id}/permissions")]
    [RequirePermission("Users.Edit")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UpdatePermissions(
        string id,
        [FromBody] RolePermissionsUpdateRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new RolePermissionsUpdateResult(false, "Invalid request body."));
        }

        var result = await _rolePermissionService.UpdateRolePermissionsAsync(id, request, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
