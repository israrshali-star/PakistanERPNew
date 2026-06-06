using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorBillConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_VendorBills_Companies_CompanyId' AND parent_object_id = OBJECT_ID(N'dbo.VendorBills'))
    ALTER TABLE [dbo].[VendorBills] DROP CONSTRAINT [FK_VendorBills_Companies_CompanyId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_VendorBills_JournalEntries_JournalEntryId' AND parent_object_id = OBJECT_ID(N'dbo.VendorBills'))
    ALTER TABLE [dbo].[VendorBills] DROP CONSTRAINT [FK_VendorBills_JournalEntries_JournalEntryId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_VendorBills_Vendors_VendorId' AND parent_object_id = OBJECT_ID(N'dbo.VendorBills'))
    ALTER TABLE [dbo].[VendorBills] DROP CONSTRAINT [FK_VendorBills_Vendors_VendorId];
");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_VendorBills_Number' AND object_id = OBJECT_ID(N'dbo.VendorBills'))
    DROP INDEX [UX_VendorBills_Number] ON [dbo].[VendorBills];

IF COL_LENGTH('dbo.VendorBills', 'RefNo') = -1
    ALTER TABLE [dbo].[VendorBills] ALTER COLUMN [RefNo] nvarchar(100) NULL;
IF COL_LENGTH('dbo.VendorBills', 'BillNumber') = -1
    ALTER TABLE [dbo].[VendorBills] ALTER COLUMN [BillNumber] nvarchar(50) NOT NULL;
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_VendorBills_Number' AND object_id = OBJECT_ID(N'dbo.VendorBills'))
    CREATE UNIQUE INDEX [UX_VendorBills_Number] ON [dbo].[VendorBills]([CompanyId], [BillNumber]) WHERE [IsDeleted] = 0;
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_VendorBills_Companies_CompanyId')
    ALTER TABLE [dbo].[VendorBills] WITH CHECK ADD CONSTRAINT [FK_VendorBills_Companies_CompanyId]
        FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies]([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_VendorBills_JournalEntries_JournalEntryId')
    ALTER TABLE [dbo].[VendorBills] WITH CHECK ADD CONSTRAINT [FK_VendorBills_JournalEntries_JournalEntryId]
        FOREIGN KEY([JournalEntryId]) REFERENCES [dbo].[JournalEntries]([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_VendorBills_Vendors_VendorId')
    ALTER TABLE [dbo].[VendorBills] WITH CHECK ADD CONSTRAINT [FK_VendorBills_Vendors_VendorId]
        FOREIGN KEY([VendorId]) REFERENCES [dbo].[Vendors]([Id]);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_VendorBills_Companies_CompanyId' AND parent_object_id = OBJECT_ID(N'dbo.VendorBills'))
    ALTER TABLE [dbo].[VendorBills] DROP CONSTRAINT [FK_VendorBills_Companies_CompanyId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_VendorBills_JournalEntries_JournalEntryId' AND parent_object_id = OBJECT_ID(N'dbo.VendorBills'))
    ALTER TABLE [dbo].[VendorBills] DROP CONSTRAINT [FK_VendorBills_JournalEntries_JournalEntryId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_VendorBills_Vendors_VendorId' AND parent_object_id = OBJECT_ID(N'dbo.VendorBills'))
    ALTER TABLE [dbo].[VendorBills] DROP CONSTRAINT [FK_VendorBills_Vendors_VendorId];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_VendorBills_Number' AND object_id = OBJECT_ID(N'dbo.VendorBills'))
    DROP INDEX [UX_VendorBills_Number] ON [dbo].[VendorBills];
");

            migrationBuilder.AlterColumn<string>(
                name: "RefNo",
                table: "VendorBills",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BillNumber",
                table: "VendorBills",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBills_Companies_CompanyId",
                table: "VendorBills",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBills_JournalEntries_JournalEntryId",
                table: "VendorBills",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBills_Vendors_VendorId",
                table: "VendorBills",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
