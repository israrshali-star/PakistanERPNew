using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorBillLineStackLot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.VendorBillLines', 'LotNo') IS NULL
    ALTER TABLE [dbo].[VendorBillLines] ADD [LotNo] nvarchar(50) NULL;
IF COL_LENGTH('dbo.VendorBillLines', 'StackNo') IS NULL
    ALTER TABLE [dbo].[VendorBillLines] ADD [StackNo] nvarchar(50) NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LotNo",
                table: "VendorBillLines");

            migrationBuilder.DropColumn(
                name: "StackNo",
                table: "VendorBillLines");
        }
    }
}
