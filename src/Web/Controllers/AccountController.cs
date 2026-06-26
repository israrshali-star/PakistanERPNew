using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.ViewModels;
using System.Security.Claims;

namespace PakistanAccountingERP.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly ICompanyService _companyService;
    private readonly ICurrentCompanyService _currentCompany;

    public AccountController(
        IAuthService authService,
        ICompanyService companyService,
        ICurrentCompanyService currentCompany)
    {
        _authService = authService;
        _companyService = companyService;
        _currentCompany = currentCompany;
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

            var model = new LoginViewModel
            {
                ReturnUrl = returnUrl,
                RequireCompanySelection = true,
                Email = User.FindFirstValue(ClaimTypes.Email)
                    ?? User.Identity?.Name
                    ?? string.Empty
            };
            await PopulateUserCompaniesAsync(model, cancellationToken);
            return View(model);
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return await CompleteCompanySelectionAsync(model, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(string.Empty, "Email and password are required.");
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

        if (model.CompanyId > 0)
        {
            return await CompleteCompanySelectionAsync(model, cancellationToken);
        }

        model.RequireCompanySelection = true;
        await PopulateUserCompaniesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> SelectCompany(string? returnUrl, CancellationToken cancellationToken)
    {
        var companies = await _companyService.GetUserCompaniesAsync(cancellationToken);
        var current = await _companyService.GetCurrentCompanyAsync(cancellationToken);

        if (current is not null && _currentCompany.IsCompanyLocked)
        {
            return RedirectAfterCompanySelected(returnUrl);
        }

        if (current is null && companies.Count == 1)
        {
            var autoSelected = await _companyService.SetCurrentCompanyAsync(
                companies[0].Id,
                lockSession: true,
                cancellationToken);

            if (autoSelected)
            {
                return RedirectAfterCompanySelected(returnUrl);
            }
        }

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

        var success = await _companyService.SetCurrentCompanyAsync(
            model.CompanyId,
            lockSession: true,
            cancellationToken);

        if (!success)
        {
            if (_currentCompany.IsCompanyLocked)
            {
                return RedirectAfterCompanySelected(model.ReturnUrl);
            }

            ModelState.AddModelError(nameof(model.CompanyId), "You do not have access to the selected company.");
            return await ReloadSelectCompanyViewAsync(model, cancellationToken);
        }

        return RedirectAfterCompanySelected(model.ReturnUrl);
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

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(
        [FromForm] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword)
            || string.IsNullOrWhiteSpace(request.NewPassword)
            || string.IsNullOrWhiteSpace(request.ConfirmPassword))
        {
            return BadRequest(new { message = "All password fields are required." });
        }

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return BadRequest(new { message = "New password and confirmation do not match." });
        }

        var result = await _authService.ChangePasswordAsync(
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { message = result.ErrorMessage ?? "Could not change password." });
        }

        return Ok(new { message = "Password changed successfully." });
    }

    private async Task<IActionResult> CompleteCompanySelectionAsync(
        LoginViewModel model,
        CancellationToken cancellationToken)
    {
        if (model.CompanyId <= 0)
        {
            ModelState.AddModelError(nameof(model.CompanyId), "Please select a company.");
            model.RequireCompanySelection = true;
            await PopulateUserCompaniesAsync(model, cancellationToken);
            return View(nameof(Login), model);
        }

        var companySet = await _companyService.SetCurrentCompanyAsync(
            model.CompanyId,
            lockSession: true,
            cancellationToken);

        if (!companySet)
        {
            ModelState.AddModelError(
                nameof(model.CompanyId),
                "You do not have access to the selected company.");
            model.RequireCompanySelection = true;
            await PopulateUserCompaniesAsync(model, cancellationToken);
            return View(nameof(Login), model);
        }

        return RedirectAfterCompanySelected(model.ReturnUrl);
    }

    private IActionResult RedirectAfterCompanySelected(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    private async Task PopulateUserCompaniesAsync(LoginViewModel model, CancellationToken cancellationToken)
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

        if (model.CompanyId <= 0)
        {
            var defaultCompany = companies.FirstOrDefault(c => c.IsDefault) ?? companies.FirstOrDefault();
            model.CompanyId = defaultCompany?.Id ?? 0;
        }
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
