using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class SalesInvoiceLineConfiguration : IEntityTypeConfiguration<SalesInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceLine> builder)
    {
        builder.ToTable("SalesInvoiceLines");
        builder.Property(x => x.CartonDescription).HasMaxLength(50);
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.Cartons).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.Price).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.TaxRate).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.TaxAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.Discount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.LineTotal).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.HasIndex(x => x.SalesInvoiceId).HasDatabaseName("IX_SalesInvoiceLines_SalesInvoiceId");
        builder.HasIndex(x => x.ItemId).HasDatabaseName("IX_SalesInvoiceLines_ItemId");
        builder.HasOne(x => x.SalesInvoice)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Item)
            .WithMany(x => x.SalesInvoiceLines)
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
