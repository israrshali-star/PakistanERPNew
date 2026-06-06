using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class ScenarioTypeConfiguration : IEntityTypeConfiguration<ScenarioType>
{
    public void Configure(EntityTypeBuilder<ScenarioType> builder)
    {
        builder.ToTable("ScenarioTypes");
        builder.HasKey(x => x.ScenarioId);
        builder.Property(x => x.ScenarioId).ValueGeneratedNever();
        builder.Property(x => x.Code).HasColumnName("ScenarioType").HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_ScenarioTypes_Type");
    }
}
