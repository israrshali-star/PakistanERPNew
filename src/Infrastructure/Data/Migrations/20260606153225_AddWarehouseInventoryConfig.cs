using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PakistanAccountingERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseInventoryConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_InventoryTransactions_Companies_CompanyId' AND parent_object_id = OBJECT_ID(N'dbo.InventoryTransactions'))
    ALTER TABLE [dbo].[InventoryTransactions] DROP CONSTRAINT [FK_InventoryTransactions_Companies_CompanyId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_InventoryTransactions_Items_ItemId' AND parent_object_id = OBJECT_ID(N'dbo.InventoryTransactions'))
    ALTER TABLE [dbo].[InventoryTransactions] DROP CONSTRAINT [FK_InventoryTransactions_Items_ItemId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_InventoryTransactions_Warehouses_WarehouseId' AND parent_object_id = OBJECT_ID(N'dbo.InventoryTransactions'))
    ALTER TABLE [dbo].[InventoryTransactions] DROP CONSTRAINT [FK_InventoryTransactions_Warehouses_WarehouseId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ItemCategories_Companies_CompanyId' AND parent_object_id = OBJECT_ID(N'dbo.ItemCategories'))
    ALTER TABLE [dbo].[ItemCategories] DROP CONSTRAINT [FK_ItemCategories_Companies_CompanyId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Warehouses_Companies_CompanyId' AND parent_object_id = OBJECT_ID(N'dbo.Warehouses'))
    ALTER TABLE [dbo].[Warehouses] DROP CONSTRAINT [FK_Warehouses_Companies_CompanyId];
");

            // Schema_v6.sql uses IX_InventoryTx_ItemId_Date and never creates IX_InventoryTransactions_ItemId.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventoryTransactions_ItemId' AND object_id = OBJECT_ID(N'dbo.InventoryTransactions'))
    DROP INDEX [IX_InventoryTransactions_ItemId] ON [dbo].[InventoryTransactions];
");

            // Use raw SQL instead of AlterColumn — EF tries to drop UX_Warehouses_Code before altering Code even when the index does not exist yet.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Warehouses_Code' AND object_id = OBJECT_ID(N'dbo.Warehouses'))
    DROP INDEX [UX_Warehouses_Code] ON [dbo].[Warehouses];

IF COL_LENGTH('dbo.Warehouses', 'Name') = -1
    ALTER TABLE [dbo].[Warehouses] ALTER COLUMN [Name] nvarchar(200) NOT NULL;
IF COL_LENGTH('dbo.Warehouses', 'Code') = -1
    ALTER TABLE [dbo].[Warehouses] ALTER COLUMN [Code] nvarchar(50) NOT NULL;

IF COL_LENGTH('dbo.ItemCategories', 'Name') = -1
    ALTER TABLE [dbo].[ItemCategories] ALTER COLUMN [Name] nvarchar(200) NOT NULL;

IF COL_LENGTH('dbo.InventoryTransactions', 'ReferenceNo') = -1
    ALTER TABLE [dbo].[InventoryTransactions] ALTER COLUMN [ReferenceNo] nvarchar(50) NULL;
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Warehouses_Code' AND object_id = OBJECT_ID(N'dbo.Warehouses'))
    CREATE UNIQUE INDEX [UX_Warehouses_Code] ON [dbo].[Warehouses]([CompanyId], [Code]) WHERE [IsDeleted] = 0;
");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventoryTx_ItemId_Date' AND object_id = OBJECT_ID(N'dbo.InventoryTransactions'))
    DROP INDEX [IX_InventoryTx_ItemId_Date] ON [dbo].[InventoryTransactions];
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventoryTransactions_ItemId_Date' AND object_id = OBJECT_ID(N'dbo.InventoryTransactions'))
    CREATE INDEX [IX_InventoryTransactions_ItemId_Date] ON [dbo].[InventoryTransactions]([ItemId], [TransactionDate]);
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_InventoryTransactions_Companies_CompanyId')
    ALTER TABLE [dbo].[InventoryTransactions] WITH CHECK ADD CONSTRAINT [FK_InventoryTransactions_Companies_CompanyId]
        FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies]([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_InventoryTransactions_Items_ItemId')
    ALTER TABLE [dbo].[InventoryTransactions] WITH CHECK ADD CONSTRAINT [FK_InventoryTransactions_Items_ItemId]
        FOREIGN KEY([ItemId]) REFERENCES [dbo].[Items]([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_InventoryTransactions_Warehouses_WarehouseId')
    ALTER TABLE [dbo].[InventoryTransactions] WITH CHECK ADD CONSTRAINT [FK_InventoryTransactions_Warehouses_WarehouseId]
        FOREIGN KEY([WarehouseId]) REFERENCES [dbo].[Warehouses]([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ItemCategories_Companies_CompanyId')
    ALTER TABLE [dbo].[ItemCategories] WITH CHECK ADD CONSTRAINT [FK_ItemCategories_Companies_CompanyId]
        FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies]([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Warehouses_Companies_CompanyId')
    ALTER TABLE [dbo].[Warehouses] WITH CHECK ADD CONSTRAINT [FK_Warehouses_Companies_CompanyId]
        FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies]([Id]);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_Companies_CompanyId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_Items_ItemId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_Warehouses_WarehouseId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_ItemCategories_Companies_CompanyId",
                table: "ItemCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_Warehouses_Companies_CompanyId",
                table: "Warehouses");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Warehouses_Code' AND object_id = OBJECT_ID(N'dbo.Warehouses'))
    DROP INDEX [UX_Warehouses_Code] ON [dbo].[Warehouses];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventoryTransactions_ItemId_Date' AND object_id = OBJECT_ID(N'dbo.InventoryTransactions'))
    DROP INDEX [IX_InventoryTransactions_ItemId_Date] ON [dbo].[InventoryTransactions];
");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Warehouses",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Warehouses",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ItemCategories",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNo",
                table: "InventoryTransactions",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ItemId",
                table: "InventoryTransactions",
                column: "ItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_Companies_CompanyId",
                table: "InventoryTransactions",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_Items_ItemId",
                table: "InventoryTransactions",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_Warehouses_WarehouseId",
                table: "InventoryTransactions",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ItemCategories_Companies_CompanyId",
                table: "ItemCategories",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Warehouses_Companies_CompanyId",
                table: "Warehouses",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
