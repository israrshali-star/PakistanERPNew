using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using PakistanAccountingERP.Infrastructure.Identity;

namespace PakistanAccountingERP.Infrastructure.Data.Seed;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var applyMigrations = configuration.GetSection("Database").GetValue("ApplyMigrationsOnStartup", true);
        var seedOnStartup = configuration.GetSection("Database").GetValue("SeedOnStartup", true);

        if (applyMigrations)
        {
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} pending EF migration(s): {Migrations}",
                    pending.Count, string.Join(", ", pending));
                await context.Database.MigrateAsync(cancellationToken);
            }
            else
            {
                logger.LogInformation("No pending EF migrations.");
            }
        }
        else
        {
            logger.LogInformation("Skipping EF migrations (Database:ApplyMigrationsOnStartup = false).");
        }

        if (!seedOnStartup)
        {
            logger.LogInformation("Skipping seed (Database:SeedOnStartup = false).");
            return;
        }

        await SeedLookupsAsync(context, cancellationToken);
        await SeedRolesAsync(roleManager, cancellationToken);
        await EnsurePermissionsAsync(context, cancellationToken);
        await EnsureSuperAdminRolePermissionsAsync(context, cancellationToken);
        await EnsureDemoAdminAccessAsync(context, userManager, cancellationToken);

        if (await context.Companies.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Company data already exists; ensuring chart of accounts and demo items.");
            await EnsureChartOfAccountsAsync(context, cancellationToken);
            await EnsureDemoItemsAsync(context, cancellationToken);
            await EnsureDemoItemStackLotAsync(context, cancellationToken);
            await EnsureDemoWarehousesAsync(context, cancellationToken);
            await EnsureDemoFiscalYearsAsync(context, cancellationToken);
            await EnsureDemoBanksAsync(context, cancellationToken);
            await EnsureTaxSettingsAsync(context, cancellationToken);
            return;
        }

        var company = await SeedCompanyAsync(context, cancellationToken);
        await SeedTaxSettingAsync(context, company.Id, cancellationToken);
        await SeedChartOfAccountsAsync(context, company.Id, cancellationToken);
        await EnsureDemoItemsAsync(context, company.Id, cancellationToken);
        await EnsureDemoWarehousesAsync(context, company.Id, cancellationToken);
        await EnsureDemoFiscalYearsAsync(context, company.Id, cancellationToken);
        await EnsureDemoBanksAsync(context, company.Id, cancellationToken);
        await SeedAdminUserAsync(context, userManager, company.Id, cancellationToken);

        logger.LogInformation("Database seed completed.");
    }

    private static async Task SeedLookupsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (!await context.Provinces.AnyAsync(cancellationToken))
        {
            await IdentityInsertHelper.SaveWithExplicitKeysAsync(context, "Provinces", () =>
            {
                context.Provinces.AddRange(LookupSeedData.GetProvinces());
                return Task.CompletedTask;
            }, cancellationToken);
        }

        if (!await context.UnitsOfMeasure.AnyAsync(cancellationToken))
        {
            await IdentityInsertHelper.SaveWithExplicitKeysAsync(context, "UnitsOfMeasure", () =>
            {
                context.UnitsOfMeasure.AddRange(LookupSeedData.GetUnitsOfMeasure());
                return Task.CompletedTask;
            }, cancellationToken);
        }

        if (!await context.AccountTypes.AnyAsync(cancellationToken))
        {
            await IdentityInsertHelper.SaveWithExplicitKeysAsync(context, "AccountTypes", () =>
            {
                context.AccountTypes.AddRange(LookupSeedData.GetAccountTypes());
                return Task.CompletedTask;
            }, cancellationToken);
        }

        if (!await context.SubAccountTypes.AnyAsync(cancellationToken))
        {
            await IdentityInsertHelper.SaveWithExplicitKeysAsync(context, "SubAccountTypes", () =>
            {
                context.SubAccountTypes.AddRange(LookupSeedData.GetSubAccountTypes());
                return Task.CompletedTask;
            }, cancellationToken);
        }

        if (!await context.ScenarioTypes.AnyAsync(cancellationToken))
        {
            await IdentityInsertHelper.SaveWithExplicitKeysAsync(context, "ScenarioTypes", () =>
            {
                context.ScenarioTypes.AddRange(LookupSeedData.GetScenarioTypes());
                return Task.CompletedTask;
            }, cancellationToken);
        }
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager, CancellationToken cancellationToken)
    {
        var roles = new (string Id, string Name)[]
        {
            (SeedConstants.SuperAdminRoleId, "SuperAdmin"),
            (SeedConstants.AdminRoleId, "Admin"),
            (SeedConstants.AccountantRoleId, "Accountant"),
            (SeedConstants.SalesUserRoleId, "SalesUser"),
            (SeedConstants.PurchaseUserRoleId, "PurchaseUser"),
            (SeedConstants.ReportsUserRoleId, "ReportsUser")
        };

        foreach (var (id, name) in roles)
        {
            if (await roleManager.RoleExistsAsync(name))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole(name) { Id = id, NormalizedName = name.ToUpperInvariant() });
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Failed to create role {name}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }
    }

    private static async Task EnsurePermissionsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Permissions.AnyAsync(cancellationToken))
        {
            return;
        }

        var permissions = PermissionSeedData.GetPermissions();

        await IdentityInsertHelper.SaveWithExplicitKeysAsync(context, "Permissions", () =>
        {
            context.Permissions.AddRange(permissions);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    private static async Task EnsureSuperAdminRolePermissionsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var allPermissions = await context.Permissions
            .AsNoTracking()
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (allPermissions.Count == 0)
        {
            return;
        }

        var assigned = await context.RolePermissions
            .Where(rp => rp.RoleId == SeedConstants.SuperAdminRoleId)
            .Select(rp => rp.PermissionId)
            .ToListAsync(cancellationToken);

        var missing = allPermissions.Except(assigned).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        context.RolePermissions.AddRange(missing.Select(permissionId => new RolePermission
        {
            RoleId = SeedConstants.SuperAdminRoleId,
            PermissionId = permissionId,
            CanView = true,
            CanCreate = true,
            CanEdit = true,
            CanDelete = true
        }));

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Company> SeedCompanyAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var company = new Company
        {
            CompanyName = SeedConstants.DemoCompanyName,
            NTN = SeedConstants.DemoCompanyNtn,
            ProvinceId = 1,
            Address = "Lahore, Pakistan",
            Phone = "+92-300-0000000",
            Email = "info@democompany.com",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system"
        };

        context.Companies.Add(company);
        await context.SaveChangesAsync(cancellationToken);
        return company;
    }

    private static async Task SeedTaxSettingAsync(AppDbContext context, int companyId, CancellationToken cancellationToken)
    {
        context.TaxSettings.Add(new TaxSetting
        {
            CompanyId = companyId,
            GroupName = "Standard Rate",
            Description = "Default Pakistan sales tax rates",
            SalesTaxRate = 18m,
            UnregisteredSalesTaxRate = 22m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system"
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedChartOfAccountsAsync(AppDbContext context, int companyId, CancellationToken cancellationToken)
    {
        var accounts = ChartOfAccountsSeedData.GetDefaultAccounts(companyId, DateTime.UtcNow);
        context.ChartOfAccounts.AddRange(accounts);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureChartOfAccountsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var companyIds = await context.Companies
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (companyIds.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var companyId in companyIds)
        {
            var existingNumbers = await context.ChartOfAccounts
                .Where(a => a.CompanyId == companyId)
                .Select(a => a.AccountNumber)
                .ToListAsync(cancellationToken);

            var existingSet = existingNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = ChartOfAccountsSeedData.GetDefaultAccounts(companyId, now)
                .Where(a => !existingSet.Contains(a.AccountNumber))
                .ToList();

            if (missing.Count == 0)
            {
                continue;
            }

            context.ChartOfAccounts.AddRange(missing);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task EnsureDemoItemsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var companyIds = await context.Companies.Select(c => c.Id).ToListAsync(cancellationToken);
        foreach (var companyId in companyIds)
        {
            await EnsureDemoItemsAsync(context, companyId, cancellationToken);
        }
    }

    private static async Task EnsureDemoItemsAsync(AppDbContext context, int companyId, CancellationToken cancellationToken)
    {
        var hasItems = await context.Items.AnyAsync(i => i.CompanyId == companyId, cancellationToken);
        if (hasItems)
        {
            return;
        }

        var pcsUnitId = await context.UnitsOfMeasure
            .Where(u => u.Symbol == "PCS")
            .Select(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (pcsUnitId == 0)
        {
            pcsUnitId = 3;
        }

        context.Items.Add(new Item
        {
            CompanyId = companyId,
            ItemType = ItemType.Goods,
            ItemCode = "ITEM-0001",
            ItemName = "General Product",
            StackNo = "STK-001",
            LotNo = "LOT-001",
            Description = "Demo item for sales invoices",
            HSCode = "0101.2100",
            UnitOfMeasureId = pcsUnitId,
            PurchaseRate = 800m,
            SaleRate = 1000m,
            MinimumStock = 0m,
            ReorderLevel = 0m,
            CurrentStock = 0m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system"
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureDemoItemStackLotAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var items = await context.Items
            .Where(i => i.ItemCode == "ITEM-0001" && (i.StackNo == "" || i.LotNo == ""))
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.StackNo))
            {
                item.StackNo = "STK-001";
            }

            if (string.IsNullOrWhiteSpace(item.LotNo))
            {
                item.LotNo = "LOT-001";
            }
        }

        if (items.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task EnsureDemoWarehousesAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var companyIds = await context.Companies.Select(c => c.Id).ToListAsync(cancellationToken);
        foreach (var companyId in companyIds)
        {
            await EnsureDemoWarehousesAsync(context, companyId, cancellationToken);
        }
    }

    private static async Task EnsureDemoWarehousesAsync(AppDbContext context, int companyId, CancellationToken cancellationToken)
    {
        var hasWarehouse = await context.Warehouses.AnyAsync(w => w.CompanyId == companyId, cancellationToken);
        if (hasWarehouse)
        {
            return;
        }

        context.Warehouses.Add(new Warehouse
        {
            CompanyId = companyId,
            Code = "WH-0001",
            Name = "Main Warehouse",
            Address = "Default storage location",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system"
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureTaxSettingsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var companyIds = await context.Companies.Select(c => c.Id).ToListAsync(cancellationToken);
        foreach (var companyId in companyIds)
        {
            var hasTaxSetting = await context.TaxSettings.AnyAsync(t => t.CompanyId == companyId, cancellationToken);
            if (hasTaxSetting)
            {
                continue;
            }

            await SeedTaxSettingAsync(context, companyId, cancellationToken);
        }
    }

    private static async Task EnsureDemoBanksAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var companyIds = await context.Companies.Select(c => c.Id).ToListAsync(cancellationToken);
        foreach (var companyId in companyIds)
        {
            await EnsureDemoBanksAsync(context, companyId, cancellationToken);
        }
    }

    private static async Task EnsureDemoFiscalYearsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var companyIds = await context.Companies.Select(c => c.Id).ToListAsync(cancellationToken);
        foreach (var companyId in companyIds)
        {
            await EnsureDemoFiscalYearsAsync(context, companyId, cancellationToken);
        }
    }

    private static async Task EnsureDemoFiscalYearsAsync(AppDbContext context, int companyId, CancellationToken cancellationToken)
    {
        var hasFiscalYear = await context.FiscalYears.AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        if (hasFiscalYear)
        {
            return;
        }

        var year = DateTime.UtcNow.Year;
        var start = new DateTime(year, 7, 1);
        var end = new DateTime(year + 1, 6, 30);

        context.FiscalYears.Add(new FiscalYear
        {
            CompanyId = companyId,
            Code = $"FY{start:yyyy}-{end:yy}",
            Name = $"{start:yyyy}-{end:yyyy} Fiscal Year",
            StartDate = start,
            EndDate = end,
            IsActive = true,
            IsClosed = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system"
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureDemoBanksAsync(AppDbContext context, int companyId, CancellationToken cancellationToken)
    {
        var hasBank = await context.Banks.AnyAsync(b => b.CompanyId == companyId, cancellationToken);
        if (hasBank)
        {
            return;
        }

        var cashAccountId = await context.ChartOfAccounts
            .Where(a => a.CompanyId == companyId && a.AccountNumber == GlAccountNumbers.CashInHand)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        context.Banks.Add(new Bank
        {
            CompanyId = companyId,
            BankName = "HBL",
            AccountTitle = "Demo Company Account",
            AccountNumber = "BANK-0001",
            IBAN = "PK00HABB0000001123456702",
            ChartOfAccountId = cashAccountId,
            OpeningBalance = 0m,
            CurrentBalance = 0m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system"
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureDemoAdminAccessAsync(
        AppDbContext context,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        var admin = await userManager.FindByEmailAsync(SeedConstants.AdminEmail);
        if (admin is null)
        {
            return;
        }

        if (!await userManager.IsInRoleAsync(admin, "SuperAdmin"))
        {
            await userManager.AddToRoleAsync(admin, "SuperAdmin");
        }

        var companyIds = await context.Companies
            .Where(c => !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (companyIds.Count == 0)
        {
            return;
        }

        var linkedCompanyIds = await context.UserCompanies
            .Where(uc => uc.UserId == admin.Id)
            .Select(uc => uc.CompanyId)
            .ToListAsync(cancellationToken);

        var added = false;
        foreach (var companyId in companyIds.Where(id => !linkedCompanyIds.Contains(id)))
        {
            context.UserCompanies.Add(new UserCompany
            {
                UserId = admin.Id,
                CompanyId = companyId
            });
            added = true;
        }

        if (added)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SeedAdminUserAsync(
        AppDbContext context,
        UserManager<ApplicationUser> userManager,
        int companyId,
        CancellationToken cancellationToken)
    {
        var admin = new ApplicationUser
        {
            UserName = SeedConstants.AdminEmail,
            Email = SeedConstants.AdminEmail,
            EmailConfirmed = true,
            FullName = "System Administrator",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(admin, SeedConstants.AdminPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Failed to create admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(admin, "SuperAdmin");

        context.UserCompanies.Add(new UserCompany
        {
            UserId = admin.Id,
            CompanyId = companyId
        });

        await context.SaveChangesAsync(cancellationToken);
    }
}
