using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;
using PakistanAccountingERP.Web.Models;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Dashboard.View")]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ICurrentUserService _currentUser;
    private readonly ICompanyService _companyService;

    public HomeController(
        ILogger<HomeController> logger,
        ICurrentUserService currentUser,
        ICompanyService companyService)
    {
        _logger = logger;
        _currentUser = currentUser;
        _companyService = companyService;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewBag.UserName = _currentUser.UserName;
        ViewBag.Company = await _companyService.GetCurrentCompanyAsync(cancellationToken);
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
