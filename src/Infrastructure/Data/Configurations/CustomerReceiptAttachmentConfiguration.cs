using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class CustomerReceiptAttachmentConfiguration : IEntityTypeConfiguration<CustomerReceiptAttachment>
{
    public void Configure(EntityTypeBuilder<CustomerReceiptAttachment> builder)
    {
        builder.ToTable("CustomerReceiptAttachments");
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StoredFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RelativePath).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => x.CustomerReceiptId).HasDatabaseName("IX_CustomerReceiptAttachments_CustomerReceiptId");
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_CustomerReceiptAttachments_CompanyId");
        builder.HasOne(x => x.CustomerReceipt)
            .WithMany(r => r.Attachments)
            .HasForeignKey(x => x.CustomerReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
