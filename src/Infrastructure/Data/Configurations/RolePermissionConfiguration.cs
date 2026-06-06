using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

public class PermissionEntityConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Key).HasMaxLength(450).IsRequired();
        builder.HasIndex(x => x.Key).IsUnique().HasDatabaseName("UX_Permissions_Key");
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.Property(x => x.RoleId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.CanView).HasDefaultValue(false);
        builder.Property(x => x.CanCreate).HasDefaultValue(false);
        builder.Property(x => x.CanEdit).HasDefaultValue(false);
        builder.Property(x => x.CanDelete).HasDefaultValue(false);
        builder.HasOne(x => x.Permission)
            .WithMany(x => x.RolePermissions)
            .HasForeignKey(x => x.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.RoleId, x.PermissionId })
            .IsUnique()
            .HasDatabaseName("UX_RolePermissions");
    }
}
