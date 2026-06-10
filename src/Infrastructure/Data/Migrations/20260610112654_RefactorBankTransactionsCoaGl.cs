using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefactorBankTransactionsCoaGl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChartOfAccountId",
                table: "BankTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CounterChartOfAccountId",
                table: "BankTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "JournalEntryId",
                table: "BankTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyName",
                table: "BankTransactions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TransferToChartOfAccountId",
                table: "BankTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE bt
                SET bt.ChartOfAccountId = b.ChartOfAccountId
                FROM BankTransactions bt
                INNER JOIN Banks b ON b.Id = bt.BankId
                WHERE bt.ChartOfAccountId IS NULL AND b.ChartOfAccountId IS NOT NULL;

                UPDATE bt
                SET bt.TransferToChartOfAccountId = b.ChartOfAccountId
                FROM BankTransactions bt
                INNER JOIN Banks b ON b.Id = bt.TransferToBankId
                WHERE bt.TransferToChartOfAccountId IS NULL AND b.ChartOfAccountId IS NOT NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "ChartOfAccountId",
                table: "BankTransactions",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_ChartOfAccountId",
                table: "BankTransactions",
                column: "ChartOfAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_CounterChartOfAccountId",
                table: "BankTransactions",
                column: "CounterChartOfAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_JournalEntryId",
                table: "BankTransactions",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_TransferToChartOfAccountId",
                table: "BankTransactions",
                column: "TransferToChartOfAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_BankTransactions_ChartOfAccounts_ChartOfAccountId",
                table: "BankTransactions",
                column: "ChartOfAccountId",
                principalTable: "ChartOfAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BankTransactions_ChartOfAccounts_CounterChartOfAccountId",
                table: "BankTransactions",
                column: "CounterChartOfAccountId",
                principalTable: "ChartOfAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BankTransactions_ChartOfAccounts_TransferToChartOfAccountId",
                table: "BankTransactions",
                column: "TransferToChartOfAccountId",
                principalTable: "ChartOfAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BankTransactions_JournalEntries_JournalEntryId",
                table: "BankTransactions",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankTransactions_ChartOfAccounts_ChartOfAccountId",
                table: "BankTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_BankTransactions_ChartOfAccounts_CounterChartOfAccountId",
                table: "BankTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_BankTransactions_ChartOfAccounts_TransferToChartOfAccountId",
                table: "BankTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_BankTransactions_JournalEntries_JournalEntryId",
                table: "BankTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_ChartOfAccountId",
                table: "BankTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_CounterChartOfAccountId",
                table: "BankTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_JournalEntryId",
                table: "BankTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_TransferToChartOfAccountId",
                table: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "ChartOfAccountId",
                table: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "CounterChartOfAccountId",
                table: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "JournalEntryId",
                table: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "PartyName",
                table: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "TransferToChartOfAccountId",
                table: "BankTransactions");
        }
    }
}
