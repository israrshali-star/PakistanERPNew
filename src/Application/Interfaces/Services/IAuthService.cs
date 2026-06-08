namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Handles user sign-in, sign-out, and post-login company selection.
/// </summary>
public interface IAuthService
{
    /// <summary>Authenticates user. Company selection is completed on a separate step after login.</summary>
    Task<DTOs.AuthResult> LoginAsync(string email, string password, bool rememberMe, CancellationToken cancellationToken = default);

    /// <summary>Signs out the current user and clears company session.</summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);
}
