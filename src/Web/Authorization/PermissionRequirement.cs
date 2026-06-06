using Microsoft.AspNetCore.Authorization;

namespace PakistanAccountingERP.Web.Authorization;

public class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permissionKey)
    {
        PermissionKey = permissionKey;
    }

    public string PermissionKey { get; }
}
