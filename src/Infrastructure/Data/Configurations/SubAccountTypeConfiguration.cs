using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class SubAccountTypeConfiguration : IEntityTypeConfiguration<SubAccountType>
{
    public void Configure(EntityTypeBuilder<SubAccountType> builder)
    {
        builder.ToTable("SubAccountTypes");
        builder.HasKey(x => x.SubTypeId);
        builder.Property(x => x.SubTypeId).ValueGeneratedNever();
        builder.Property(x => x.SubTypeName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.SubTypeCode).HasMaxLength(50).IsRequired();
        builder.HasOne(x => x.AccountType)
            .WithMany(x => x.SubAccountTypes)
            .HasForeignKey(x => x.TypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TypeId, x.SubTypeCode })
            .IsUnique()
            .HasDatabaseName("UX_SubAccountTypes_Code");
    }
}
