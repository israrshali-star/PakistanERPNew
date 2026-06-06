namespace PakistanAccountingERP.Application.Interfaces;

/// <summary>
/// Provides the authenticated user context for the current HTTP request.
/// </summary>
public interface ICurrentUserService
{
    string? UserId { get; }
    string? UserName { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    IReadOnlyList<string> Roles { get; }
    string? IpAddress { get; }
}
