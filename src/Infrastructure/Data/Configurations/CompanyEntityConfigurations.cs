using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Infrastructure.Identity;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Companies");
        builder.Property(x => x.CompanyName).HasMaxLength(450).IsRequired();
        builder.Property(x => x.IsDefault).HasDefaultValue(false);
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.HasOne(x => x.Province).WithMany().HasForeignKey(x => x.ProvinceId);
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.Property(x => x.BuyerId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.OpeningBalance).HasDefaultValue(0m);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_Customers_CompanyId");
        builder.HasIndex(x => x.ScenarioId).HasDatabaseName("IX_Customers_ScenarioId");
        builder.HasIndex(x => new { x.CompanyId, x.BuyerId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Customers_BuyerId");
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Province)
            .WithMany()
            .HasForeignKey(x => x.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ScenarioType)
            .WithMany(x => x.Customers)
            .HasForeignKey(x => x.ScenarioId)
            .HasPrincipalKey(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> builder)
    {
        builder.ToTable("Vendors");
        builder.Property(x => x.VendorCode).HasMaxLength(450).IsRequired();
        builder.Property(x => x.DefaultSalesTaxRate).HasDefaultValue(18m);
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_Vendors_CompanyId");
        builder.HasIndex(x => new { x.CompanyId, x.VendorCode })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Vendors_Code");
    }
}

public class ItemCategoryConfiguration : IEntityTypeConfiguration<ItemCategory>
{
    public void Configure(EntityTypeBuilder<ItemCategory> builder)
    {
        builder.ToTable("ItemCategories");
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_ItemCategories_CompanyId");
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("Items");
        builder.Property(x => x.ItemCode).HasMaxLength(450).IsRequired();
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_Items_CompanyId");
        builder.HasIndex(x => new { x.CompanyId, x.ItemCode })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Items_Code");
    }
}

public class UserCompanyConfiguration : IEntityTypeConfiguration<UserCompany>
{
    public void Configure(EntityTypeBuilder<UserCompany> builder)
    {
        builder.ToTable("UserCompanies");
        builder.HasKey(x => new { x.UserId, x.CompanyId });
        builder.HasOne<ApplicationUser>()
            .WithMany(x => x.UserCompanies)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Company)
            .WithMany(x => x.UserCompanies)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETDATE()");
    }
}

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_Warehouses_CompanyId");
        builder.HasIndex(x => new { x.CompanyId, x.Code })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Warehouses_Code");
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class FiscalYearConfiguration : IEntityTypeConfiguration<FiscalYear>
{
    public void Configure(EntityTypeBuilder<FiscalYear> builder)
    {
        builder.ToTable("FiscalYears");
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_FiscalYears_CompanyId");
        builder.HasIndex(x => new { x.CompanyId, x.Code })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_FiscalYears_Code");
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("InventoryTransactions");
        builder.Property(x => x.ReferenceNo).HasMaxLength(50);
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_InventoryTransactions_CompanyId");
        builder.HasIndex(x => new { x.ItemId, x.TransactionDate }).HasDatabaseName("IX_InventoryTransactions_ItemId_Date");
        builder.HasOne(x => x.Item)
            .WithMany(i => i.InventoryTransactions)
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Warehouse)
            .WithMany(w => w.InventoryTransactions)
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CustomerReceiptConfiguration : IEntityTypeConfiguration<CustomerReceipt>
{
    public void Configure(EntityTypeBuilder<CustomerReceipt> builder)
    {
        builder.ToTable("CustomerReceipts");
        builder.Property(x => x.ReceiptNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ChequeNumber).HasMaxLength(50);
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_CustomerReceipts_CompanyId");
        builder.HasIndex(x => new { x.CompanyId, x.ReceiptNumber })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_CustomerReceipts_Number");
        builder.HasOne(x => x.Customer)
            .WithMany(c => c.CustomerReceipts)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Bank)
            .WithMany()
            .HasForeignKey(x => x.BankId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DepositedBankTransaction)
            .WithMany(t => t.DepositedCustomerReceipts)
            .HasForeignKey(x => x.DepositedBankTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CompanyId, x.IsDeposited })
            .HasDatabaseName("IX_CustomerReceipts_CompanyId_IsDeposited");
    }
}

public class VendorPaymentConfiguration : IEntityTypeConfiguration<VendorPayment>
{
    public void Configure(EntityTypeBuilder<VendorPayment> builder)
    {
        builder.ToTable("VendorPayments");
        builder.Property(x => x.PaymentNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ChequeNumber).HasMaxLength(50);
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_VendorPayments_CompanyId");
        builder.HasIndex(x => new { x.CompanyId, x.PaymentNumber })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_VendorPayments_Number");
        builder.HasOne(x => x.Vendor)
            .WithMany(v => v.VendorPayments)
            .HasForeignKey(x => x.VendorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Bank)
            .WithMany()
            .HasForeignKey(x => x.BankId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BankConfiguration : IEntityTypeConfiguration<Bank>
{
    public void Configure(EntityTypeBuilder<Bank> builder)
    {
        builder.ToTable("Banks");
        builder.Property(x => x.BankName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AccountTitle).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AccountNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.IBAN).HasMaxLength(50);
        builder.Property(x => x.NextChequeNumber).HasMaxLength(50);
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_Banks_CompanyId");
        builder.HasIndex(x => new { x.CompanyId, x.AccountNumber })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Banks_AccountNumber");
        builder.HasOne(x => x.ChartOfAccount)
            .WithMany(c => c.Banks)
            .HasForeignKey(x => x.ChartOfAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BankReconciliationConfiguration : IEntityTypeConfiguration<BankReconciliation>
{
    public void Configure(EntityTypeBuilder<BankReconciliation> builder)
    {
        builder.ToTable("BankReconciliations");
        builder.HasIndex(x => x.BankId).HasDatabaseName("IX_BankReconciliations_BankId");
        builder.HasOne(x => x.Bank)
            .WithMany(b => b.BankReconciliations)
            .HasForeignKey(x => x.BankId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.ToTable("BankTransactions");
        builder.Property(x => x.ChequeNumber).HasMaxLength(50);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.HasOne(x => x.Bank)
            .WithMany(b => b.BankTransactions)
            .HasForeignKey(x => x.BankId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TransferToBank)
            .WithMany(x => x.TransferToTransactions)
            .HasForeignKey(x => x.TransferToBankId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ChartOfAccount)
            .WithMany()
            .HasForeignKey(x => x.ChartOfAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TransferToChartOfAccount)
            .WithMany()
            .HasForeignKey(x => x.TransferToChartOfAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CounterChartOfAccount)
            .WithMany()
            .HasForeignKey(x => x.CounterChartOfAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Customer)
            .WithMany(c => c.WriteChequePayments)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Vendor)
            .WithMany(v => v.WriteChequePayments)
            .HasForeignKey(x => x.VendorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.JournalEntry)
            .WithMany()
            .HasForeignKey(x => x.JournalEntryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.PartyName).HasMaxLength(200);
        builder.Property(x => x.CustomerBalanceEffect).HasPrecision(18, 2);
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BankId, x.TransactionDate }).HasDatabaseName("IX_BankTransactions_BankId_Date");
        builder.HasIndex(x => x.ChartOfAccountId).HasDatabaseName("IX_BankTransactions_ChartOfAccountId");
        builder.HasIndex(x => x.TransferToBankId).HasDatabaseName("IX_BankTransactions_TransferTo");
        builder.HasIndex(x => x.JournalEntryId).HasDatabaseName("IX_BankTransactions_JournalEntryId");
    }
}

public class DatabaseBackupHistoryConfiguration : IEntityTypeConfiguration<DatabaseBackupHistory>
{
    public void Configure(EntityTypeBuilder<DatabaseBackupHistory> builder)
    {
        builder.ToTable("DatabaseBackupHistories");
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.FilePath).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(1000);
        builder.HasIndex(x => x.StartedAt).HasDatabaseName("IX_DatabaseBackupHistories_StartedAt");
    }
}

public class DataExportHistoryConfiguration : IEntityTypeConfiguration<DataExportHistory>
{
    public void Configure(EntityTypeBuilder<DataExportHistory> builder)
    {
        builder.ToTable("DataExportHistories");
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.FilePath).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(1000);
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_DataExportHistories_CompanyId");
        builder.HasIndex(x => x.StartedAt).HasDatabaseName("IX_DataExportHistories_StartedAt");
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
