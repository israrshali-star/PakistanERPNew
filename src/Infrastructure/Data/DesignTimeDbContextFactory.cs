using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PakistanAccountingERP.Infrastructure.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost;Database=PakistanAccountingERP;Trusted_Connection=True;TrustServerCertificate=True;");

        return new AppDbContext(optionsBuilder.Options);
    }
}
