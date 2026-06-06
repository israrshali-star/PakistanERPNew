using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Aligns the EF model with databases that already use Customers.ScenarioId as the FK
    /// (for example Schema_v6). No schema change is required when ScenarioTypeScenarioId is absent.
    /// </remarks>
    public partial class FixCustomerScenarioForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
