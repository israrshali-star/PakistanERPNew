using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Settings.View")]
public class SystemHealthController : Controller
{
    public IActionResult Index()
    {
        ViewData["BreadcrumbParent"] = "Settings";
        ViewData["ApplicationVersion"] = HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["AppSettings:Version"] ?? "1.0.0";
        return View();
    }
}

[Authorize]
[ApiController]
[Route("api/system-health")]
public class SystemHealthApiController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;

    public SystemHealthApiController(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    [HttpGet]
    [RequirePermission("Settings.View")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var report = await _healthCheckService.CheckHealthAsync(cancellationToken: cancellationToken);
        var payload = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                duration = entry.Value.Duration.TotalMilliseconds,
                tags = entry.Value.Tags,
                error = entry.Value.Exception?.Message
            })
        };

        return report.Status == HealthStatus.Healthy
            ? Ok(payload)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
    }
}
