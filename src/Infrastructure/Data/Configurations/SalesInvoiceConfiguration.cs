using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    public void Configure(EntityTypeBuilder<SalesInvoice> builder)
    {
        builder.ToTable("SalesInvoices");
        builder.Property(x => x.InvoiceNumber).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ShippingAddress).HasMaxLength(500).IsRequired();
        builder.Property(x => x.SubTotal).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.DiscountAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.TaxAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.FurtherTax).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.FED).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.ExtraTax).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.WithholdingTax).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.NetTotal).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.HasIndex(x => new { x.CompanyId, x.InvoiceDate }).HasDatabaseName("IX_SalesInvoices_CompanyId_Date");
        builder.HasIndex(x => x.CustomerId).HasDatabaseName("IX_SalesInvoices_CustomerId");
        builder.HasIndex(x => x.ScenarioId).HasDatabaseName("IX_SalesInvoices_ScenarioId");
        builder.HasIndex(x => x.ProvinceId).HasDatabaseName("IX_SalesInvoices_ProvinceId");
        builder.HasIndex(x => new { x.CompanyId, x.InvoiceNumber })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_SalesInvoices_Number");
        builder.HasOne(x => x.ScenarioType)
            .WithMany(x => x.SalesInvoices)
            .HasForeignKey(x => x.ScenarioId)
            .HasPrincipalKey(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
