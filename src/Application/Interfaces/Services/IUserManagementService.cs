using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IUserManagementService
{
    Task<DataTableResponse<UserListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<UserListItemDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupDto>> GetCompanyLookupsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRoleLookupsAsync(CancellationToken cancellationToken = default);

    Task<UserSaveResult> CreateAsync(UserCreateRequest request, CancellationToken cancellationToken = default);

    Task<UserSaveResult> UpdateAsync(string id, UserUpdateRequest request, CancellationToken cancellationToken = default);

    Task<UserActionResult> ResetPasswordAsync(string id, string newPassword, CancellationToken cancellationToken = default);

    Task<UserActionResult> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
