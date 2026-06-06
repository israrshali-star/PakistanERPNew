using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceLineStackLot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_Items_ItemId",
                table: "SalesInvoiceLines");

            migrationBuilder.AlterColumn<decimal>(
                name: "TaxRate",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "TaxAmount",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "LineTotal",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Discount",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Cartons",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AddColumn<string>(
                name: "LotNo",
                table: "SalesInvoiceLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StackNo",
                table: "SalesInvoiceLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_Items_ItemId",
                table: "SalesInvoiceLines",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_Items_ItemId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "LotNo",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "StackNo",
                table: "SalesInvoiceLines");

            migrationBuilder.AlterColumn<decimal>(
                name: "TaxRate",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "TaxAmount",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "LineTotal",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "Discount",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "Cartons",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldDefaultValue: 0m);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_Items_ItemId",
                table: "SalesInvoiceLines",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
