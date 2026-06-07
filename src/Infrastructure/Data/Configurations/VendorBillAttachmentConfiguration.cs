using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class VendorBillAttachmentConfiguration : IEntityTypeConfiguration<VendorBillAttachment>
{
    public void Configure(EntityTypeBuilder<VendorBillAttachment> builder)
    {
        builder.ToTable("VendorBillAttachments");
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StoredFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RelativePath).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => x.VendorBillId).HasDatabaseName("IX_VendorBillAttachments_VendorBillId");
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_VendorBillAttachments_CompanyId");
        builder.HasOne(x => x.VendorBill)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.VendorBillId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
