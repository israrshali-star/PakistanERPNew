using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixUnclearedChequeReceiptData : Migration
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
                DELETE jel
                FROM JournalEntryLines jel
                INNER JOIN JournalEntries je ON jel.JournalEntryId = je.Id
                INNER JOIN CustomerReceipts cr ON je.ReferenceType = N'CustomerReceipt' AND je.ReferenceId = cr.Id
                WHERE cr.PaymentMethod = 2
                  AND cr.ClearedAt IS NULL
                  AND cr.IsDeleted = 0;

                DELETE je
                FROM JournalEntries je
                INNER JOIN CustomerReceipts cr ON je.ReferenceType = N'CustomerReceipt' AND je.ReferenceId = cr.Id
                WHERE cr.PaymentMethod = 2
                  AND cr.ClearedAt IS NULL
                  AND cr.IsDeleted = 0;
                """);

            migrationBuilder.Sql("""
                DELETE jel
                FROM JournalEntryLines jel
                INNER JOIN JournalEntries je ON jel.JournalEntryId = je.Id
                INNER JOIN CustomerReceipts cr ON je.ReferenceType = N'BankTransaction' AND je.ReferenceId = cr.DepositedBankTransactionId
                WHERE cr.PaymentMethod = 2
                  AND cr.ClearedAt IS NULL
                  AND cr.DepositedBankTransactionId IS NOT NULL
                  AND cr.IsDeleted = 0;

                DELETE je
                FROM JournalEntries je
                INNER JOIN CustomerReceipts cr ON je.ReferenceType = N'BankTransaction' AND je.ReferenceId = cr.DepositedBankTransactionId
                WHERE cr.PaymentMethod = 2
                  AND cr.ClearedAt IS NULL
                  AND cr.DepositedBankTransactionId IS NOT NULL
                  AND cr.IsDeleted = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
