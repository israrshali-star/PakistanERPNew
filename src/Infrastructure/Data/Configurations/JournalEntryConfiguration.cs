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
        builder.HasIndex(x => new { x.CompanyId, x.ReferenceType, x.ReferenceId })
            .HasDatabaseName("IX_JournalEntries_CompanyId_Ref");
    }
}

public class JournalEntryLineConfiguration : IEntityTypeConfiguration<JournalEntryLine>
{
    public void Configure(EntityTypeBuilder<JournalEntryLine> builder)
    {
        // Keep line visibility aligned with both required, soft-deletable principals.
        builder.HasQueryFilter(x =>
            !x.JournalEntry.IsDeleted
            && !x.ChartOfAccount.IsDeleted);
    }
}
