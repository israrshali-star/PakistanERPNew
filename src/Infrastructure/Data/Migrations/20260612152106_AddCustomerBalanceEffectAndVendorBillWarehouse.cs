using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerBalanceEffectAndVendorBillWarehouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "VendorBills",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CustomerBalanceEffect",
                table: "BankTransactions",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_WarehouseId",
                table: "VendorBills",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBills_Warehouses_WarehouseId",
                table: "VendorBills",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                UPDATE [BankTransactions]
                SET [CustomerBalanceEffect] = [Amount]
                WHERE [CustomerId] IS NOT NULL
                  AND [TransactionType] = 2
                  AND [IsDeleted] = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VendorBills_Warehouses_WarehouseId",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_VendorBills_WarehouseId",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "CustomerBalanceEffect",
                table: "BankTransactions");
        }
    }
}
