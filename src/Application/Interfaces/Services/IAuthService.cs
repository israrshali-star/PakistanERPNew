namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Handles user sign-in and sign-out.
/// </summary>
public interface IAuthService
{
    /// <summary>Authenticates user. Company is selected on the login page before redirecting to the app.</summary>
    Task<DTOs.AuthResult> LoginAsync(string email, string password, bool rememberMe, CancellationToken cancellationToken = default);

    /// <summary>Signs out the current user and clears company session.</summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);
}
