using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingAddressAndChequeReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShippingAddress",
                table: "SalesInvoices",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE SalesInvoices
                SET ShippingAddress = ISNULL(NULLIF(LTRIM(RTRIM(BuyerAddress)), ''), N'-')
                WHERE ShippingAddress = N'';
                """);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAt",
                table: "CustomerReceipts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnedBy",
                table: "CustomerReceipts",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShippingAddress",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "ReturnedAt",
                table: "CustomerReceipts");

            migrationBuilder.DropColumn(
                name: "ReturnedBy",
                table: "CustomerReceipts");
        }
    }
}
