using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class VendorBillConfiguration : IEntityTypeConfiguration<VendorBill>
{
    public void Configure(EntityTypeBuilder<VendorBill> builder)
    {
        builder.ToTable("VendorBills");
        builder.Property(x => x.BillNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RefNo).HasMaxLength(100);
        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_VendorBills_CompanyId");
        builder.HasIndex(x => new { x.CompanyId, x.BillNumber })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_VendorBills_Number");
        builder.HasOne(x => x.Vendor)
            .WithMany(v => v.VendorBills)
            .HasForeignKey(x => x.VendorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.JournalEntry)
            .WithMany(j => j.VendorBills)
            .HasForeignKey(x => x.JournalEntryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
