using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Seed;

public static class PermissionSeedData
{
    private static readonly string[] Modules =
    [
        "Dashboard", "ChartOfAccounts", "Customers", "Sales", "Vendors", "Purchase",
        "Items", "Inventory", "Banking", "Reports", "Settings", "Users", "AuditLogs", "JournalEntries"
    ];

    private static readonly string[] Actions = ["View", "Create", "Edit", "Delete"];

    public static IReadOnlyList<Permission> GetPermissions()
    {
        var permissions = new List<Permission>();
        var id = 1;

        foreach (var module in Modules)
        {
            foreach (var action in Actions)
            {
                permissions.Add(new Permission
                {
                    Id = id++,
                    Module = module,
                    Action = action,
                    Key = $"{module}.{action}",
                    Description = $"{action} access for {module}"
                });
            }
        }

        return permissions;
    }
}
