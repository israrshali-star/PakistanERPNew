using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.ViewModels;

namespace PakistanAccountingERP.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly ICompanyService _companyService;

    public AccountController(IAuthService authService, ICompanyService companyService)
    {
        _authService = authService;
        _companyService = companyService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var current = await _companyService.GetCurrentCompanyAsync(cancellationToken);
            if (current is not null)
            {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Index", "Home");
            }

            return RedirectToAction(nameof(SelectCompany), new { returnUrl });
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _authService.LoginAsync(
            model.Email,
            model.Password,
            model.RememberMe,
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Login failed.");
            return View(model);
        }

        return RedirectToAction(nameof(SelectCompany), new { returnUrl = model.ReturnUrl });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> SelectCompany(string? returnUrl, CancellationToken cancellationToken)
    {
        var companies = await _companyService.GetUserCompaniesAsync(cancellationToken);
        var current = await _companyService.GetCurrentCompanyAsync(cancellationToken);
        var defaultCompany = companies.FirstOrDefault(c => c.IsDefault) ?? companies.FirstOrDefault();

        var model = new SelectCompanyViewModel
        {
            ReturnUrl = returnUrl,
            CompanyId = current?.Id ?? defaultCompany?.Id ?? 0,
            Companies = companies
                .Select(c => new CompanyOptionViewModel
                {
                    Id = c.Id,
                    CompanyName = c.CompanyName,
                    IsDefault = c.IsDefault
                })
                .ToList()
        };

        return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectCompany(SelectCompanyViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await ReloadSelectCompanyViewAsync(model, cancellationToken);
        }

        var success = await _companyService.SetCurrentCompanyAsync(model.CompanyId, cancellationToken);
        if (!success)
        {
            ModelState.AddModelError(nameof(model.CompanyId), "You do not have access to the selected company.");
            return await ReloadSelectCompanyViewAsync(model, cancellationToken);
        }

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(cancellationToken);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }

    private async Task<IActionResult> ReloadSelectCompanyViewAsync(
        SelectCompanyViewModel model,
        CancellationToken cancellationToken)
    {
        var companies = await _companyService.GetUserCompaniesAsync(cancellationToken);
        model.Companies = companies
            .Select(c => new CompanyOptionViewModel
            {
                Id = c.Id,
                CompanyName = c.CompanyName,
                IsDefault = c.IsDefault
            })
            .ToList();

        return View(model);
    }
}
