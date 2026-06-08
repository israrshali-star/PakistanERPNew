using Microsoft.AspNetCore.Identity;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Infrastructure.Identity;

namespace PakistanAccountingERP.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentCompanyService _currentCompany;

    public AuthService(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuditService auditService,
        ICurrentUserService currentUser,
        ICurrentCompanyService currentCompany)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _auditService = auditService;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public async Task<AuthResult> LoginAsync(
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
        {
            return AuthResult.Failure("Invalid email or password.");
        }

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName!,
            password,
            rememberMe,
            lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return AuthResult.Failure("Account is locked. Try again later.");
        }

        if (!result.Succeeded)
        {
            return AuthResult.Failure("Invalid email or password.");
        }

        await _currentCompany.ClearCompanyAsync(cancellationToken);

        await _auditService.LogLoginAsync(
            user.Id,
            user.FullName ?? user.Email ?? email,
            _currentUser.IpAddress ?? "unknown",
            cancellationToken);

        return AuthResult.Success();
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _currentCompany.ClearCompanyAsync(cancellationToken);
        await _signInManager.SignOutAsync();
    }
}
