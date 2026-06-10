using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerReceiptDepositTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DepositedBankTransactionId",
                table: "CustomerReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeposited",
                table: "CustomerReceipts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_CompanyId_IsDeposited",
                table: "CustomerReceipts",
                columns: new[] { "CompanyId", "IsDeposited" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_DepositedBankTransactionId",
                table: "CustomerReceipts",
                column: "DepositedBankTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerReceipts_BankTransactions_DepositedBankTransactionId",
                table: "CustomerReceipts",
                column: "DepositedBankTransactionId",
                principalTable: "BankTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerReceipts_BankTransactions_DepositedBankTransactionId",
                table: "CustomerReceipts");

            migrationBuilder.DropIndex(
                name: "IX_CustomerReceipts_CompanyId_IsDeposited",
                table: "CustomerReceipts");

            migrationBuilder.DropIndex(
                name: "IX_CustomerReceipts_DepositedBankTransactionId",
                table: "CustomerReceipts");

            migrationBuilder.DropColumn(
                name: "DepositedBankTransactionId",
                table: "CustomerReceipts");

            migrationBuilder.DropColumn(
                name: "IsDeposited",
                table: "CustomerReceipts");
        }
    }
}
