using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankTransactionPartyLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "BankTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VendorId",
                table: "BankTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_CustomerId",
                table: "BankTransactions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_VendorId",
                table: "BankTransactions",
                column: "VendorId");

            migrationBuilder.AddForeignKey(
                name: "FK_BankTransactions_Customers_CustomerId",
                table: "BankTransactions",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BankTransactions_Vendors_VendorId",
                table: "BankTransactions",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankTransactions_Customers_CustomerId",
                table: "BankTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_BankTransactions_Vendors_VendorId",
                table: "BankTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_CustomerId",
                table: "BankTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_VendorId",
                table: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "BankTransactions");
        }
    }
}
