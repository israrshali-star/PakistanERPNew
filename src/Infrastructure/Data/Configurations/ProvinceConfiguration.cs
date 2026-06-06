using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class ProvinceConfiguration : IEntityTypeConfiguration<Province>
{
    public void Configure(EntityTypeBuilder<Province> builder)
    {
        builder.ToTable("Provinces");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
    }
}

public class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.ToTable("UnitsOfMeasure");
        builder.Property(x => x.Id).ValueGeneratedNever();
    }
}
