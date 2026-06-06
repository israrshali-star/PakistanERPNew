using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class ChartOfAccountConfiguration : IEntityTypeConfiguration<ChartOfAccount>
{
    public void Configure(EntityTypeBuilder<ChartOfAccount> builder)
    {
        builder.ToTable("ChartOfAccounts");
        builder.Property(x => x.AccountNumber).HasMaxLength(450).IsRequired();
        builder.Property(x => x.OpeningBalance).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasOne(x => x.AccountType)
            .WithMany(x => x.ChartOfAccounts)
            .HasForeignKey(x => x.TypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SubAccountType)
            .WithMany(x => x.ChartOfAccounts)
            .HasForeignKey(x => x.SubTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ParentAccount)
            .WithMany(x => x.ChildAccounts)
            .HasForeignKey(x => x.ParentAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_ChartOfAccounts_CompanyId");
        builder.HasIndex(x => x.ParentAccountId).HasDatabaseName("IX_ChartOfAccounts_ParentAccountId");
    }
}
