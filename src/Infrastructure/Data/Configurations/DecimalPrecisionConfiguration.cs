using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Configurations;

/// <summary>
/// Applies decimal(18,2) to all decimal properties on transactional entities.
/// </summary>
public static class DecimalPrecisionConfiguration
{
    public static void ApplyDecimalPrecision(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                {
                    property.SetColumnType("decimal(18,2)");
                }
            }
        }
    }
}
