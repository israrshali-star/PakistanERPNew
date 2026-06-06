using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class VendorBillLineConfiguration : IEntityTypeConfiguration<VendorBillLine>
{
    public void Configure(EntityTypeBuilder<VendorBillLine> builder)
    {
        builder.ToTable("VendorBillLines");
        builder.Property(x => x.StackNo).HasMaxLength(50);
        builder.Property(x => x.LotNo).HasMaxLength(50);
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.Cartons).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.Rate).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_VendorBillLines_ItemOrDesc",
            "[ItemId] IS NOT NULL OR ([Description] IS NOT NULL AND LEN([Description]) > 0)"));
    }
}
