using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RepairDepositedChequesWithoutJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE CustomerReceipts
                SET Status = 1
                WHERE PaymentMethod = 2
                  AND ClearedAt IS NULL
                  AND IsDeleted = 0;
                """);

            migrationBuilder.Sql("""
                UPDATE cr
                SET IsDeposited = 0,
                    DepositedBankTransactionId = NULL
                FROM CustomerReceipts cr
                WHERE cr.PaymentMethod = 2
                  AND cr.ClearedAt IS NULL
                  AND cr.IsDeposited = 1
                  AND cr.IsDeleted = 0
                  AND cr.DepositedBankTransactionId IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM JournalEntries je
                      WHERE je.ReferenceType = N'BankTransaction'
                        AND je.ReferenceId = cr.DepositedBankTransactionId
                        AND je.Status = 2
                        AND je.IsDeleted = 0
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
