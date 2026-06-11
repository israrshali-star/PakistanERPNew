using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankTransactionPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "BankTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE bt
                SET PaymentMethod = CASE
                    WHEN coa.AccountNumber = N'10015' THEN 1
                    WHEN bt.ChequeNumber IS NOT NULL AND LTRIM(RTRIM(bt.ChequeNumber)) <> N'' THEN 2
                    ELSE 3
                END
                FROM BankTransactions bt
                INNER JOIN ChartOfAccounts coa ON coa.Id = bt.ChartOfAccountId
                WHERE bt.TransactionType = 2
                  AND bt.IsDeleted = 0
                  AND bt.PaymentMethod IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "BankTransactions");
        }
    }
}
