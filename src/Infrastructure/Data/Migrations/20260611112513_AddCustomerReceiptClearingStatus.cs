using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerReceiptClearingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClearedAt",
                table: "CustomerReceipts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClearedBy",
                table: "CustomerReceipts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "CustomerReceipts",
                type: "int",
                nullable: false,
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClearedAt",
                table: "CustomerReceipts");

            migrationBuilder.DropColumn(
                name: "ClearedBy",
                table: "CustomerReceipts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "CustomerReceipts");
        }
    }
}
