using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PakistanAccountingERP.Application.Interfaces;

namespace PakistanAccountingERP.Infrastructure.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public string? UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? UserName => User?.Identity?.Name
        ?? User?.FindFirstValue(ClaimTypes.Name)
        ?? User?.FindFirstValue("FullName");

    public string? Email => User?.FindFirstValue(ClaimTypes.Email);

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public IReadOnlyList<string> Roles =>
        User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
