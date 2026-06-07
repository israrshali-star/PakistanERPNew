using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Infrastructure.Data;
using System.Text.Json;

namespace PakistanAccountingERP.Infrastructure.Services;

public class RolePermissionManagementService : IRolePermissionManagementService
{
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AppDbContext _context;
    private readonly IPermissionService _permissionService;
    private readonly IAuditService _auditService;
    private readonly ILogger<RolePermissionManagementService> _logger;

    public RolePermissionManagementService(
        RoleManager<IdentityRole> roleManager,
        AppDbContext context,
        IPermissionService permissionService,
        IAuditService auditService,
        ILogger<RolePermissionManagementService> logger)
    {
        _roleManager = roleManager;
        _context = context;
        _permissionService = permissionService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RoleListItemDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        return await _roleManager.Roles
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new RoleListItemDto(x.Id, x.Name!))
            .ToListAsync(cancellationToken);
    }

    public async Task<RolePermissionsDto?> GetRolePermissionsAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.Roles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == roleId, cancellationToken);
        if (role is null)
        {
            return null;
        }

        var assigned = await _context.RolePermissions
            .AsNoTracking()
            .Where(x => x.RoleId == roleId)
            .ToDictionaryAsync(x => x.PermissionId, cancellationToken);

        var matrix = await _context.Permissions
            .AsNoTracking()
            .OrderBy(x => x.Module)
            .ThenBy(x => x.Action)
            .Select(p => new PermissionMatrixRowDto(
                p.Id,
                p.Module,
                p.Action,
                p.Key,
                assigned.ContainsKey(p.Id) && IsAllowedByAction(assigned[p.Id], p.Action)))
            .ToListAsync(cancellationToken);

        return new RolePermissionsDto(role.Id, role.Name!, matrix);
    }

    public async Task<RolePermissionsUpdateResult> UpdateRolePermissionsAsync(
        string roleId,
        RolePermissionsUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role is null)
        {
            return new RolePermissionsUpdateResult(false, "Role not found.");
        }

        var requestedMap = request.Permissions
            .GroupBy(x => x.PermissionId)
            .ToDictionary(x => x.Key, x => x.Last().Allowed);

        var allPermissions = await _context.Permissions
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var existing = await _context.RolePermissions
            .Where(x => x.RoleId == roleId)
            .ToListAsync(cancellationToken);

        var oldSnapshot = JsonSerializer.Serialize(existing.Select(x => new
        {
            x.PermissionId,
            x.CanView,
            x.CanCreate,
            x.CanEdit,
            x.CanDelete
        }));

        var existingMap = existing.ToDictionary(x => x.PermissionId);

        foreach (var permission in allPermissions)
        {
            var allowed = requestedMap.TryGetValue(permission.Id, out var isAllowed) && isAllowed;
            if (!existingMap.TryGetValue(permission.Id, out var rolePermission))
            {
                rolePermission = new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permission.Id
                };
                _context.RolePermissions.Add(rolePermission);
                existingMap[permission.Id] = rolePermission;
            }

            ApplyActionPermission(rolePermission, permission.Action, allowed);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _permissionService.InvalidateCacheAsync(cancellationToken: cancellationToken);

        var newSnapshot = JsonSerializer.Serialize(existingMap.Values.Select(x => new
        {
            x.PermissionId,
            x.CanView,
            x.CanCreate,
            x.CanEdit,
            x.CanDelete
        }));

        await TryAuditAsync(roleId, oldSnapshot, newSnapshot, cancellationToken);
        return new RolePermissionsUpdateResult(true, "Permissions updated successfully.");
    }

    private static bool IsAllowedByAction(RolePermission rolePermission, string action)
    {
        return action switch
        {
            "View" => rolePermission.CanView,
            "Create" => rolePermission.CanCreate,
            "Edit" => rolePermission.CanEdit,
            "Delete" => rolePermission.CanDelete,
            _ => false
        };
    }

    private static void ApplyActionPermission(RolePermission rolePermission, string action, bool allowed)
    {
        switch (action)
        {
            case "View":
                rolePermission.CanView = allowed;
                break;
            case "Create":
                rolePermission.CanCreate = allowed;
                break;
            case "Edit":
                rolePermission.CanEdit = allowed;
                break;
            case "Delete":
                rolePermission.CanDelete = allowed;
                break;
        }
    }

    private async Task TryAuditAsync(
        string roleId,
        string? oldValue,
        string? newValue,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditService.LogAsync("Update", "RolePermissions", roleId, oldValue, newValue, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for role {RoleId}", roleId);
        }
    }
}
