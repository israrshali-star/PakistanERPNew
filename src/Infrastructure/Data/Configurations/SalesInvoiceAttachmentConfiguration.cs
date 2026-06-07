using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class SalesInvoiceAttachmentConfiguration : IEntityTypeConfiguration<SalesInvoiceAttachment>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceAttachment> builder)
    {
        builder.ToTable("SalesInvoiceAttachments");
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StoredFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RelativePath).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => x.SalesInvoiceId).HasDatabaseName("IX_SalesInvoiceAttachments_SalesInvoiceId");
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_SalesInvoiceAttachments_CompanyId");
        builder.HasOne(x => x.SalesInvoice)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
