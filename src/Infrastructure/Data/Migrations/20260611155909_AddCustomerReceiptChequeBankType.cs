using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerReceiptChequeBankType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChequeBankType",
                table: "CustomerReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE CustomerReceipts
                SET ChequeBankType = 2
                WHERE PaymentMethod = 2 AND ChequeBankType IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChequeBankType",
                table: "CustomerReceipts");
        }
    }
}
