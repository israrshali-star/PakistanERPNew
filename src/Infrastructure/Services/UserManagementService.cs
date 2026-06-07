using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Infrastructure.Data;
using PakistanAccountingERP.Infrastructure.Identity;
using System.Text.Json;

namespace PakistanAccountingERP.Infrastructure.Services;

public class UserManagementService : IUserManagementService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        AppDbContext context,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ILogger<UserManagementService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _context = context;
        _currentUser = currentUser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<DataTableResponse<UserListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var usersQuery = _userManager.Users.AsNoTracking();

        var recordsTotal = await usersQuery.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            usersQuery = usersQuery.Where(x =>
                (x.Email != null && x.Email.Contains(term)) ||
                (x.FullName != null && x.FullName.Contains(term)));
        }

        var recordsFiltered = await usersQuery.CountAsync(cancellationToken);
        usersQuery = ApplyOrdering(usersQuery, request);

        if (request.Length > 0)
        {
            usersQuery = usersQuery.Skip(request.Start).Take(request.Length);
        }

        var users = await usersQuery.ToListAsync(cancellationToken);
        var data = new List<UserListItemDto>(users.Count);

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var companyLinks = await _context.UserCompanies
                .AsNoTracking()
                .Where(x => x.UserId == user.Id)
                .Select(x => new LookupDto(x.CompanyId, x.Company.CompanyName, null))
                .ToListAsync(cancellationToken);

            data.Add(new UserListItemDto(
                user.Id,
                user.Email ?? string.Empty,
                user.FullName ?? user.Email ?? "User",
                user.IsActive,
                roles.ToList(),
                companyLinks,
                user.CreatedAt));
        }

        return new DataTableResponse<UserListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            data);
    }

    public async Task<UserListItemDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        var companyLinks = await _context.UserCompanies
            .AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .Select(x => new LookupDto(x.CompanyId, x.Company.CompanyName, null))
            .ToListAsync(cancellationToken);

        return new UserListItemDto(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName ?? user.Email ?? "User",
            user.IsActive,
            roles.ToList(),
            companyLinks,
            user.CreatedAt);
    }

    public async Task<IReadOnlyList<LookupDto>> GetCompanyLookupsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Companies
            .AsNoTracking()
            .OrderBy(x => x.CompanyName)
            .Select(x => new LookupDto(x.Id, x.CompanyName, null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetRoleLookupsAsync(CancellationToken cancellationToken = default)
    {
        return await _roleManager.Roles
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => x.Name!)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserSaveResult> CreateAsync(UserCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return new UserSaveResult(false, "Email and password are required.", null);
        }

        if (request.CompanyIds.Count == 0)
        {
            return new UserSaveResult(false, "At least one company assignment is required.", null);
        }

        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            FullName = request.FullName.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var create = await _userManager.CreateAsync(user, request.Password);
        if (!create.Succeeded)
        {
            return new UserSaveResult(false, string.Join("; ", create.Errors.Select(x => x.Description)), null);
        }

        if (request.RoleNames.Count > 0)
        {
            var addRoles = await _userManager.AddToRolesAsync(user, request.RoleNames.Distinct(StringComparer.OrdinalIgnoreCase));
            if (!addRoles.Succeeded)
            {
                return new UserSaveResult(false, string.Join("; ", addRoles.Errors.Select(x => x.Description)), null);
            }
        }

        await SyncCompaniesAsync(user.Id, request.CompanyIds, cancellationToken);

        await TryAuditAsync("Create", user.Id, null, JsonSerializer.Serialize(request), cancellationToken);
        var dto = await GetByIdAsync(user.Id, cancellationToken);
        return new UserSaveResult(true, null, dto);
    }

    public async Task<UserSaveResult> UpdateAsync(string id, UserUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return new UserSaveResult(false, "User not found.", null);
        }

        if (request.CompanyIds.Count == 0)
        {
            return new UserSaveResult(false, "At least one company assignment is required.", null);
        }

        if (string.Equals(user.Id, _currentUser.UserId, StringComparison.Ordinal))
        {
            if (!request.IsActive)
            {
                return new UserSaveResult(false, "You cannot deactivate your own account.", null);
            }
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var requestedRoles = request.RoleNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var isSuperAdminBefore = currentRoles.Contains("SuperAdmin", StringComparer.OrdinalIgnoreCase);
        var isSuperAdminAfter = requestedRoles.Contains("SuperAdmin", StringComparer.OrdinalIgnoreCase);

        if ((isSuperAdminBefore && !isSuperAdminAfter) || (isSuperAdminBefore && !request.IsActive))
        {
            var superAdminCount = await GetActiveSuperAdminCountAsync(cancellationToken);
            if (superAdminCount <= 1)
            {
                return new UserSaveResult(false, "Cannot deactivate or demote the last SuperAdmin.", null);
            }
        }

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            user.FullName,
            user.IsActive,
            Roles = currentRoles
        });

        user.FullName = request.FullName.Trim();
        user.IsActive = request.IsActive;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return new UserSaveResult(false, string.Join("; ", updateResult.Errors.Select(x => x.Description)), null);
        }

        var removeRoles = currentRoles.Except(requestedRoles, StringComparer.OrdinalIgnoreCase).ToList();
        if (removeRoles.Count > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, removeRoles);
            if (!removeResult.Succeeded)
            {
                return new UserSaveResult(false, string.Join("; ", removeResult.Errors.Select(x => x.Description)), null);
            }
        }

        var addRoles = requestedRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();
        if (addRoles.Count > 0)
        {
            var addResult = await _userManager.AddToRolesAsync(user, addRoles);
            if (!addResult.Succeeded)
            {
                return new UserSaveResult(false, string.Join("; ", addResult.Errors.Select(x => x.Description)), null);
            }
        }

        await SyncCompaniesAsync(user.Id, request.CompanyIds, cancellationToken);

        await TryAuditAsync("Update", user.Id, oldSnapshot, JsonSerializer.Serialize(request), cancellationToken);
        var dto = await GetByIdAsync(user.Id, cancellationToken);
        return new UserSaveResult(true, null, dto);
    }

    public async Task<UserActionResult> ResetPasswordAsync(string id, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return new UserActionResult(false, "User not found.");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            return new UserActionResult(false, string.Join("; ", result.Errors.Select(x => x.Description)));
        }

        await TryAuditAsync("ResetPassword", user.Id, null, "{\"changed\":true}", cancellationToken);
        return new UserActionResult(true, "Password reset successfully.");
    }

    public async Task<UserActionResult> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return new UserActionResult(false, "User not found.");
        }

        if (string.Equals(user.Id, _currentUser.UserId, StringComparison.Ordinal))
        {
            return new UserActionResult(false, "You cannot delete your own account.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains("SuperAdmin", StringComparer.OrdinalIgnoreCase))
        {
            var superAdminCount = await GetActiveSuperAdminCountAsync(cancellationToken);
            if (superAdminCount <= 1)
            {
                return new UserActionResult(false, "Cannot delete the last SuperAdmin.");
            }
        }

        _context.UserCompanies.RemoveRange(_context.UserCompanies.Where(x => x.UserId == user.Id));
        await _context.SaveChangesAsync(cancellationToken);

        var delete = await _userManager.DeleteAsync(user);
        if (!delete.Succeeded)
        {
            return new UserActionResult(false, string.Join("; ", delete.Errors.Select(x => x.Description)));
        }

        await TryAuditAsync("Delete", user.Id, JsonSerializer.Serialize(user.Email), null, cancellationToken);
        return new UserActionResult(true, "User deleted successfully.");
    }

    private async Task SyncCompaniesAsync(string userId, IReadOnlyCollection<int> companyIds, CancellationToken cancellationToken)
    {
        var distinctIds = companyIds.Distinct().ToList();
        var existing = await _context.UserCompanies
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var remove = existing.Where(x => !distinctIds.Contains(x.CompanyId)).ToList();
        if (remove.Count > 0)
        {
            _context.UserCompanies.RemoveRange(remove);
        }

        var existingIds = existing.Select(x => x.CompanyId).ToHashSet();
        var add = distinctIds
            .Where(id => !existingIds.Contains(id))
            .Select(id => new UserCompany { UserId = userId, CompanyId = id });

        _context.UserCompanies.AddRange(add);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> GetActiveSuperAdminCountAsync(CancellationToken cancellationToken)
    {
        var superAdminRole = await _roleManager.FindByNameAsync("SuperAdmin");
        if (superAdminRole is null)
        {
            return 0;
        }

        return await (
            from ur in _context.UserRoles
            join u in _context.Users on ur.UserId equals u.Id
            where ur.RoleId == superAdminRole.Id && u.IsActive
            select ur.UserId
        ).Distinct().CountAsync(cancellationToken);
    }

    private async Task TryAuditAsync(
        string action,
        string recordId,
        string? oldValue,
        string? newValue,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditService.LogAsync(action, "Users", recordId, oldValue, newValue, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for user {UserId}", recordId);
        }
    }

    private static IQueryable<ApplicationUser> ApplyOrdering(IQueryable<ApplicationUser> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(x => x.Email) : query.OrderBy(x => x.Email),
            1 => desc ? query.OrderByDescending(x => x.FullName) : query.OrderBy(x => x.FullName),
            2 => desc ? query.OrderByDescending(x => x.IsActive) : query.OrderBy(x => x.IsActive),
            3 => desc ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
            _ => query.OrderBy(x => x.Email)
        };
    }
}
