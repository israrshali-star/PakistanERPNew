using PakistanAccountingERP.Application.Interfaces;

namespace PakistanAccountingERP.Web.Middleware;

public class RequireCompanyMiddleware
{
    private static readonly string[] AllowedPathPrefixes =
    [
        "/account/selectcompany",
        "/account/logout",
        "/account/accessdenied",
        "/api/company/current",
        "/health",
        "/health/live",
        "/health/ready"
    ];

    private readonly RequestDelegate _next;

    public RequireCompanyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true || IsAllowedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var currentCompany = context.RequestServices.GetRequiredService<ICurrentCompanyService>();
        if (currentCompany.CompanyId.HasValue)
        {
            await _next(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { message = "No company selected. Please select a company to continue." });
            return;
        }

        var returnUrl = context.Request.Path + context.Request.QueryString;
        var redirect = string.IsNullOrEmpty(returnUrl) || returnUrl == "/"
            ? "/Account/SelectCompany"
            : $"/Account/SelectCompany?returnUrl={Uri.EscapeDataString(returnUrl)}";

        context.Response.Redirect(redirect);
    }

    private static bool IsAllowedPath(PathString path)
    {
        var value = path.Value ?? string.Empty;

        foreach (var prefix in AllowedPathPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
