using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Infrastructure.Identity;

namespace PakistanAccountingERP.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<AccountType> AccountTypes => Set<AccountType>();
    public DbSet<SubAccountType> SubAccountTypes => Set<SubAccountType>();
    public DbSet<ScenarioType> ScenarioTypes => Set<ScenarioType>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<UserCompany> UserCompanies => Set<UserCompany>();
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TaxSetting> TaxSettings => Set<TaxSetting>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<BankReconciliation> BankReconciliations => Set<BankReconciliation>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceLine> SalesInvoiceLines => Set<SalesInvoiceLine>();
    public DbSet<CustomerReceipt> CustomerReceipts => Set<CustomerReceipt>();
    public DbSet<VendorBill> VendorBills => Set<VendorBill>();
    public DbSet<VendorBillLine> VendorBillLines => Set<VendorBillLine>();
    public DbSet<VendorPayment> VendorPayments => Set<VendorPayment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        Configurations.DecimalPrecisionConfiguration.ApplyDecimalPrecision(builder);
        ApplySoftDeleteFilters(builder);
    }

    private static void ApplySoftDeleteFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var method = typeof(AppDbContext).GetMethod(
                nameof(SetSoftDeleteFilter),
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var generic = method.MakeGenericMethod(entityType.ClrType);
            generic.Invoke(null, [builder]);
        }
    }

    private static void SetSoftDeleteFilter<TEntity>(ModelBuilder builder)
        where TEntity : class, ISoftDelete
    {
        builder.Entity<TEntity>().HasQueryFilter(e => !e.IsDeleted);
    }
}
