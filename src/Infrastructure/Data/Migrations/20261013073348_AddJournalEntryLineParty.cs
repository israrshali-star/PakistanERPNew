using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalEntryLineParty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "JournalEntryLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyName",
                table: "JournalEntryLines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VendorId",
                table: "JournalEntryLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_CustomerId",
                table: "JournalEntryLines",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_VendorId",
                table: "JournalEntryLines",
                column: "VendorId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Customers_CustomerId",
                table: "JournalEntryLines",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Vendors_VendorId",
                table: "JournalEntryLines",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_Customers_CustomerId",
                table: "JournalEntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_Vendors_VendorId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_CustomerId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_VendorId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "PartyName",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "JournalEntryLines");
        }
    }
}
