using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemCartons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[dbo].[Items]')
                      AND name = N'Cartons')
                BEGIN
                    ALTER TABLE [dbo].[Items] ADD [Cartons] decimal(18,2) NOT NULL CONSTRAINT [DF_Items_Cartons] DEFAULT (0);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[dbo].[Items]')
                      AND name = N'Cartons')
                BEGIN
                    ALTER TABLE [dbo].[Items] DROP CONSTRAINT [DF_Items_Cartons];
                    ALTER TABLE [dbo].[Items] DROP COLUMN [Cartons];
                END
                """);
        }
    }
}
