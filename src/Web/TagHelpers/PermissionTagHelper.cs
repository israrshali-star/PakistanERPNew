using Microsoft.AspNetCore.Razor.TagHelpers;
using PakistanAccountingERP.Application.Interfaces.Services;

namespace PakistanAccountingERP.Web.TagHelpers;

/// <summary>
/// Hides element content when the user lacks the specified permission.
/// Usage: &lt;button permission="Sales.Delete"&gt;Delete&lt;/button&gt;
/// </summary>
[HtmlTargetElement(Attributes = "permission")]
public class PermissionTagHelper : TagHelper
{
    private readonly IPermissionService _permissionService;

    public PermissionTagHelper(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    [HtmlAttributeName("permission")]
    public string Permission { get; set; } = string.Empty;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrWhiteSpace(Permission))
        {
            return;
        }

        if (!await _permissionService.HasPermissionAsync(Permission))
        {
            output.SuppressOutput();
        }
    }
}
