using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankingConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankReconciliations_Banks_BankId' AND parent_object_id = OBJECT_ID(N'dbo.BankReconciliations'))
    ALTER TABLE [dbo].[BankReconciliations] DROP CONSTRAINT [FK_BankReconciliations_Banks_BankId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankReconciliations_Companies_CompanyId' AND parent_object_id = OBJECT_ID(N'dbo.BankReconciliations'))
    ALTER TABLE [dbo].[BankReconciliations] DROP CONSTRAINT [FK_BankReconciliations_Companies_CompanyId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Banks_ChartOfAccounts_ChartOfAccountId' AND parent_object_id = OBJECT_ID(N'dbo.Banks'))
    ALTER TABLE [dbo].[Banks] DROP CONSTRAINT [FK_Banks_ChartOfAccounts_ChartOfAccountId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Banks_Companies_CompanyId' AND parent_object_id = OBJECT_ID(N'dbo.Banks'))
    ALTER TABLE [dbo].[Banks] DROP CONSTRAINT [FK_Banks_Companies_CompanyId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankTransactions_Banks_BankId' AND parent_object_id = OBJECT_ID(N'dbo.BankTransactions'))
    ALTER TABLE [dbo].[BankTransactions] DROP CONSTRAINT [FK_BankTransactions_Banks_BankId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankTransactions_Companies_CompanyId' AND parent_object_id = OBJECT_ID(N'dbo.BankTransactions'))
    ALTER TABLE [dbo].[BankTransactions] DROP CONSTRAINT [FK_BankTransactions_Companies_CompanyId];
");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Banks_AccountNumber' AND object_id = OBJECT_ID(N'dbo.Banks'))
    DROP INDEX [UX_Banks_AccountNumber] ON [dbo].[Banks];

IF COL_LENGTH('dbo.BankTransactions', 'Description') = -1
    ALTER TABLE [dbo].[BankTransactions] ALTER COLUMN [Description] nvarchar(500) NULL;
IF COL_LENGTH('dbo.BankTransactions', 'ChequeNumber') = -1
    ALTER TABLE [dbo].[BankTransactions] ALTER COLUMN [ChequeNumber] nvarchar(50) NULL;

IF COL_LENGTH('dbo.Banks', 'IBAN') = -1
    ALTER TABLE [dbo].[Banks] ALTER COLUMN [IBAN] nvarchar(50) NULL;
IF COL_LENGTH('dbo.Banks', 'BankName') = -1
    ALTER TABLE [dbo].[Banks] ALTER COLUMN [BankName] nvarchar(200) NOT NULL;
IF COL_LENGTH('dbo.Banks', 'AccountTitle') = -1
    ALTER TABLE [dbo].[Banks] ALTER COLUMN [AccountTitle] nvarchar(200) NOT NULL;
IF COL_LENGTH('dbo.Banks', 'AccountNumber') = -1
    ALTER TABLE [dbo].[Banks] ALTER COLUMN [AccountNumber] nvarchar(50) NOT NULL;
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Banks_AccountNumber' AND object_id = OBJECT_ID(N'dbo.Banks'))
    CREATE UNIQUE INDEX [UX_Banks_AccountNumber] ON [dbo].[Banks]([CompanyId], [AccountNumber]) WHERE [IsDeleted] = 0;
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankReconciliations_Banks_BankId')
    ALTER TABLE [dbo].[BankReconciliations] WITH CHECK ADD CONSTRAINT [FK_BankReconciliations_Banks_BankId]
        FOREIGN KEY([BankId]) REFERENCES [dbo].[Banks]([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankReconciliations_Companies_CompanyId')
    ALTER TABLE [dbo].[BankReconciliations] WITH CHECK ADD CONSTRAINT [FK_BankReconciliations_Companies_CompanyId]
        FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies]([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Banks_ChartOfAccounts_ChartOfAccountId')
    ALTER TABLE [dbo].[Banks] WITH CHECK ADD CONSTRAINT [FK_Banks_ChartOfAccounts_ChartOfAccountId]
        FOREIGN KEY([ChartOfAccountId]) REFERENCES [dbo].[ChartOfAccounts]([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Banks_Companies_CompanyId')
    ALTER TABLE [dbo].[Banks] WITH CHECK ADD CONSTRAINT [FK_Banks_Companies_CompanyId]
        FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies]([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankTransactions_Banks_BankId')
    ALTER TABLE [dbo].[BankTransactions] WITH CHECK ADD CONSTRAINT [FK_BankTransactions_Banks_BankId]
        FOREIGN KEY([BankId]) REFERENCES [dbo].[Banks]([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankTransactions_Companies_CompanyId')
    ALTER TABLE [dbo].[BankTransactions] WITH CHECK ADD CONSTRAINT [FK_BankTransactions_Companies_CompanyId]
        FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies]([Id]);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankReconciliations_Banks_BankId' AND parent_object_id = OBJECT_ID(N'dbo.BankReconciliations'))
    ALTER TABLE [dbo].[BankReconciliations] DROP CONSTRAINT [FK_BankReconciliations_Banks_BankId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankReconciliations_Companies_CompanyId' AND parent_object_id = OBJECT_ID(N'dbo.BankReconciliations'))
    ALTER TABLE [dbo].[BankReconciliations] DROP CONSTRAINT [FK_BankReconciliations_Companies_CompanyId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Banks_ChartOfAccounts_ChartOfAccountId' AND parent_object_id = OBJECT_ID(N'dbo.Banks'))
    ALTER TABLE [dbo].[Banks] DROP CONSTRAINT [FK_Banks_ChartOfAccounts_ChartOfAccountId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Banks_Companies_CompanyId' AND parent_object_id = OBJECT_ID(N'dbo.Banks'))
    ALTER TABLE [dbo].[Banks] DROP CONSTRAINT [FK_Banks_Companies_CompanyId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankTransactions_Banks_BankId' AND parent_object_id = OBJECT_ID(N'dbo.BankTransactions'))
    ALTER TABLE [dbo].[BankTransactions] DROP CONSTRAINT [FK_BankTransactions_Banks_BankId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankTransactions_Companies_CompanyId' AND parent_object_id = OBJECT_ID(N'dbo.BankTransactions'))
    ALTER TABLE [dbo].[BankTransactions] DROP CONSTRAINT [FK_BankTransactions_Companies_CompanyId];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Banks_AccountNumber' AND object_id = OBJECT_ID(N'dbo.Banks'))
    DROP INDEX [UX_Banks_AccountNumber] ON [dbo].[Banks];
");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "BankTransactions",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ChequeNumber",
                table: "BankTransactions",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "IBAN",
                table: "Banks",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BankName",
                table: "Banks",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "AccountTitle",
                table: "Banks",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "AccountNumber",
                table: "Banks",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AddForeignKey(
                name: "FK_BankReconciliations_Banks_BankId",
                table: "BankReconciliations",
                column: "BankId",
                principalTable: "Banks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BankReconciliations_Companies_CompanyId",
                table: "BankReconciliations",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Banks_ChartOfAccounts_ChartOfAccountId",
                table: "Banks",
                column: "ChartOfAccountId",
                principalTable: "ChartOfAccounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Banks_Companies_CompanyId",
                table: "Banks",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BankTransactions_Banks_BankId",
                table: "BankTransactions",
                column: "BankId",
                principalTable: "Banks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BankTransactions_Companies_CompanyId",
                table: "BankTransactions",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
