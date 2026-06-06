using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("JournalEntries");
        builder.Property(x => x.ReferenceType).HasMaxLength(100);
        builder.Property(x => x.Status).HasDefaultValue(Domain.Enums.JournalStatus.Draft);
        builder.HasIndex(x => new { x.CompanyId, x.ReferenceType, x.ReferenceId })
            .HasDatabaseName("IX_JournalEntries_CompanyId_Ref");
    }
}
