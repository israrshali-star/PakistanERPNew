namespace PakistanAccountingERP.Application.DTOs;

public record UserListItemDto(
    string Id,
    string Email,
    string FullName,
    bool IsActive,
    IReadOnlyList<string> Roles,
    IReadOnlyList<LookupDto> Companies,
    DateTime CreatedAt);

public class UserCreateRequest
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public List<string> RoleNames { get; set; } = [];
    public List<int> CompanyIds { get; set; } = [];
}

public class UserUpdateRequest
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<string> RoleNames { get; set; } = [];
    public List<int> CompanyIds { get; set; } = [];
}

public class UserResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

public record UserSaveResult(bool Success, string? Message, UserListItemDto? User);

public record UserActionResult(bool Success, string? Message);
