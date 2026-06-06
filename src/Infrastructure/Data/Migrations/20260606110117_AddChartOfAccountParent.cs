using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChartOfAccountParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentAccountId",
                table: "ChartOfAccounts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChartOfAccounts_ParentAccountId",
                table: "ChartOfAccounts",
                column: "ParentAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChartOfAccounts_ChartOfAccounts_ParentAccountId",
                table: "ChartOfAccounts",
                column: "ParentAccountId",
                principalTable: "ChartOfAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChartOfAccounts_ChartOfAccounts_ParentAccountId",
                table: "ChartOfAccounts");

            migrationBuilder.DropIndex(
                name: "IX_ChartOfAccounts_ParentAccountId",
                table: "ChartOfAccounts");

            migrationBuilder.DropColumn(
                name: "ParentAccountId",
                table: "ChartOfAccounts");
        }
    }
}
