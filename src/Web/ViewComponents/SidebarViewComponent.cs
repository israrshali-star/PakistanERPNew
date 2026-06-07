using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Models;

namespace PakistanAccountingERP.Web.ViewComponents;

public class SidebarViewComponent : ViewComponent
{
    private readonly IPermissionService _permissionService;

    public SidebarViewComponent(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var menu = BuildMenu();
        var filtered = await FilterByPermissionAsync(menu);
        return View(filtered);
    }

    private static List<NavMenuItem> BuildMenu() =>
    [
        new() { Title = "Dashboard", Icon = "fa-gauge-high", Controller = "Home", Action = "Index", Permission = "Dashboard.View" },
        new() { Title = "Chart of Accounts", Icon = "fa-sitemap", Controller = "ChartOfAccounts", Action = "Index", Permission = "ChartOfAccounts.View" },
        new() { Title = "Journal Entries", Icon = "fa-book", Controller = "JournalEntries", Action = "Index", Permission = "JournalEntries.View" },
        new()
        {
            Title = "Sales", Icon = "fa-file-invoice-dollar",
            Children =
            [
                new() { Title = "Customers", Controller = "Customers", Action = "Index", Permission = "Customers.View" },
                new() { Title = "Create Invoice", Controller = "SalesInvoices", Action = "Create", Permission = "Sales.Create" },
                new() { Title = "Sales List", Controller = "SalesInvoices", Action = "Index", Permission = "Sales.View" },
                new() { Title = "Customer Receipts", Controller = "CustomerReceipts", Action = "Index", Permission = "Sales.View" }
            ]
        },
        new()
        {
            Title = "Purchase", Icon = "fa-cart-shopping",
            Children =
            [
                new() { Title = "Vendors", Controller = "Vendors", Action = "Index", Permission = "Vendors.View" },
                new() { Title = "Enter Bills", Controller = "VendorBills", Action = "Create", Permission = "Purchase.Create" },
                new() { Title = "Purchase Bills", Controller = "VendorBills", Action = "Index", Permission = "Purchase.View" },
                new() { Title = "Vendor Payments", Controller = "VendorPayments", Action = "Index", Permission = "Purchase.View" }
            ]
        },
        new()
        {
            Title = "Inventory", Icon = "fa-boxes-stacked",
            Children =
            [
                new() { Title = "Items", Controller = "Items", Action = "Index", Permission = "Items.View" },
                new() { Title = "Item Categories", Controller = "ItemCategories", Action = "Index", Permission = "Items.View" },
                new() { Title = "Units of Measure", Controller = "UnitsOfMeasure", Action = "Index", Permission = "Items.View" },
                new() { Title = "Warehouses", Controller = "Warehouses", Action = "Index", Permission = "Inventory.View" },
                new() { Title = "Stock Transactions", Controller = "InventoryTransactions", Action = "Index", Permission = "Inventory.View" },
                new() { Title = "Inventory Reports", Controller = "InventoryReports", Action = "Index", Permission = "Inventory.View" }
            ]
        },
        new()
        {
            Title = "Banking", Icon = "fa-building-columns",
            Children =
            [
                new() { Title = "Bank Accounts", Controller = "Banks", Action = "Index", Permission = "Banking.View" },
                new() { Title = "Transactions", Controller = "BankTransactions", Action = "Index", Permission = "Banking.View" },
                new() { Title = "Bank Reconciliation", Controller = "BankReconciliations", Action = "Index", Permission = "Banking.View" }
            ]
        },
        new()
        {
            Title = "Reports", Icon = "fa-chart-line",
            Children =
            [
                new() { Title = "Sales Reports", Controller = "SalesReports", Action = "Index", Permission = "Reports.View" },
                new() { Title = "Purchase Reports", Controller = "PurchaseReports", Action = "Index", Permission = "Reports.View" },
                new() { Title = "Financial Reports", Controller = "FinancialReports", Action = "Index", Permission = "Reports.View" },
                new() { Title = "Inventory Reports", Controller = "InventoryReports", Action = "Index", Permission = "Reports.View" }
            ]
        },
        new()
        {
            Title = "Settings", Icon = "fa-gear",
            Children =
            [
                new() { Title = "Company Settings", Controller = "CompanySettings", Action = "Index", Permission = "Settings.View" },
                new() { Title = "Tax Settings", Url = "/CompanySettings#tax", Permission = "Settings.View" },
                new() { Title = "User Management", Controller = "Users", Action = "Index", Permission = "Users.View" },
                new() { Title = "Roles & Permissions", Controller = "Roles", Action = "Index", Permission = "Users.View" },
                new() { Title = "Audit Logs", Controller = "AuditLogs", Action = "Index", Permission = "AuditLogs.View" },
                new() { Title = "Fiscal Years", Controller = "FiscalYears", Action = "Index", Permission = "Settings.View" },
                new() { Title = "Backup & Exports", Controller = "SystemJobs", Action = "Index", Permission = "Settings.View" },
                new() { Title = "System Health", Controller = "SystemHealth", Action = "Index", Permission = "Settings.View" }
            ]
        }
    ];

    private async Task<List<NavMenuItem>> FilterByPermissionAsync(List<NavMenuItem> items)
    {
        var result = new List<NavMenuItem>();

        foreach (var item in items)
        {
            if (item.IsGroup)
            {
                var children = new List<NavMenuItem>();
                foreach (var child in item.Children)
                {
                    if (await CanShowAsync(child.Permission))
                    {
                        children.Add(child);
                    }
                }

                if (children.Count > 0)
                {
                    item.Children = children;
                    result.Add(item);
                }
            }
            else if (await CanShowAsync(item.Permission))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private async Task<bool> CanShowAsync(string? permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return true;
        }

        return await _permissionService.HasPermissionAsync(permission);
    }
}
