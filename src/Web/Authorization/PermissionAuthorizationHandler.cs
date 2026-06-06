using Microsoft.AspNetCore.Authorization;
using PakistanAccountingERP.Application.Interfaces.Services;

namespace PakistanAccountingERP.Web.Authorization;

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissionService;

    public PermissionAuthorizationHandler(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            return;
        }

        if (await _permissionService.HasPermissionAsync(requirement.PermissionKey))
        {
            context.Succeed(requirement);
        }
    }
}
