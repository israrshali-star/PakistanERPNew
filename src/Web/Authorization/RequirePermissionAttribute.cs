using Microsoft.AspNetCore.Authorization;

namespace PakistanAccountingERP.Web.Authorization;

/// <summary>
/// Requires the current user to have the given permission key (e.g. "Sales.Create").
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermissionAttribute : AuthorizeAttribute, IAuthorizationRequirementData
{
    public RequirePermissionAttribute(string permissionKey)
    {
        PermissionKey = permissionKey;
    }

    public string PermissionKey { get; }

    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        yield return new PermissionRequirement(PermissionKey);
    }
}
