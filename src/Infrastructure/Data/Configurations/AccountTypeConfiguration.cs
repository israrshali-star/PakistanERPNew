using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class AccountTypeConfiguration : IEntityTypeConfiguration<AccountType>
{
    public void Configure(EntityTypeBuilder<AccountType> builder)
    {
        builder.ToTable("AccountTypes");
        builder.HasKey(x => x.TypeId);
        builder.Property(x => x.TypeId).ValueGeneratedNever();
        builder.Property(x => x.TypeCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.TypeName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETDATE()");
        builder.HasIndex(x => x.TypeCode).IsUnique().HasDatabaseName("UX_AccountTypes_Code");
    }
}
