-- ============================================================
-- Create database if it does not exist (optional — comment out
-- if you already have the database and only want to run DDL)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'PakistanAccountingERP')
BEGIN
    CREATE DATABASE [PakistanAccountingERP];
END
GO

USE [PakistanAccountingERP]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- CORRECTED SQL SCHEMA  (v6)
--
-- Changes from v5:
--   K.  ChartOfAccounts.SubTypeId + FK → SubAccountTypes
--   L.  Seed AccountTypes (6 types incl. Revenue)
--   M.  Seed SubAccountTypes (35 sub-types across all types)
--   N.  JournalEntries.ReferenceType: nvarchar(max) → nvarchar(100)
--       (required — max types cannot be index key columns)
--
-- v5 changes retained:
--   E–J  (ProvinceId FK, TransferToBankId FK, unique indexes,
--         Identity indexes, ScenarioTypes seed)
--
-- v4 changes retained:
--   A–D  (ScenarioTypes, Customers/SalesInvoices ScenarioId)
-- ============================================================

-- ----------------------------------------------------------
-- EF Migrations History
-- ----------------------------------------------------------
CREATE TABLE [dbo].[__EFMigrationsHistory](
    [MigrationId]    [nvarchar](150) NOT NULL,
    [ProductVersion] [nvarchar](32)  NOT NULL,
    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY CLUSTERED ([MigrationId] ASC)
) ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Provinces  (global lookup)
-- ----------------------------------------------------------
CREATE TABLE [dbo].[Provinces](
    [Id]       [int]           IDENTITY(1,1) NOT NULL,
    [Name]     [nvarchar](max) NOT NULL,
    [Code]     [nvarchar](max) NULL,
    [IsActive] [bit]           NOT NULL DEFAULT (1),
    CONSTRAINT [PK_Provinces] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Account Types  (global lookup)
-- ----------------------------------------------------------
CREATE TABLE [dbo].[AccountTypes](
    [TypeId]    [int]           IDENTITY(1,1) NOT NULL,
    [TypeCode]  [nvarchar](50)  NOT NULL,
    [TypeName]  [nvarchar](100) NOT NULL,
    [IsActive]  [bit]           NOT NULL DEFAULT (1),
    [CreatedAt] [datetime2](7)  NOT NULL DEFAULT (GETDATE()),
    CONSTRAINT [PK_AccountTypes] PRIMARY KEY CLUSTERED ([TypeId] ASC)
) ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Sub Account Types
-- ----------------------------------------------------------
CREATE TABLE [dbo].[SubAccountTypes](
    [SubTypeId]   [int]           IDENTITY(1,1) NOT NULL,
    [TypeId]      [int]           NOT NULL,
    [SubTypeName] [nvarchar](150) NOT NULL,
    [SubTypeCode] [nvarchar](50)  NOT NULL,
    CONSTRAINT [PK_SubAccountTypes] PRIMARY KEY CLUSTERED ([SubTypeId] ASC)
) ON [PRIMARY]
GO

ALTER TABLE [dbo].[SubAccountTypes] WITH CHECK
    ADD CONSTRAINT [FK_SubAccountTypes_AccountTypes]
    FOREIGN KEY([TypeId]) REFERENCES [dbo].[AccountTypes] ([TypeId])
GO
ALTER TABLE [dbo].[SubAccountTypes] CHECK CONSTRAINT [FK_SubAccountTypes_AccountTypes]
GO

-- ----------------------------------------------------------
-- Scenario Types  (global lookup)
-- ----------------------------------------------------------
CREATE TABLE [dbo].[ScenarioTypes](
    [ScenarioId]   [int]           IDENTITY(1,1) NOT NULL,
    [ScenarioType] [nvarchar](100) NOT NULL,
    [Description]  [nvarchar](max) NULL,
    CONSTRAINT [PK_ScenarioTypes] PRIMARY KEY CLUSTERED ([ScenarioId] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Units Of Measure  (global lookup)
-- ----------------------------------------------------------
CREATE TABLE [dbo].[UnitsOfMeasure](
    [Id]     [int]           IDENTITY(1,1) NOT NULL,
    [Name]   [nvarchar](max) NOT NULL,
    [Symbol] [nvarchar](max) NULL,
    CONSTRAINT [PK_UnitsOfMeasure] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- ASP.NET Identity tables
-- ----------------------------------------------------------
CREATE TABLE [dbo].[AspNetRoles](
    [Id]               [nvarchar](450) NOT NULL,
    [Name]             [nvarchar](256) NULL,
    [NormalizedName]   [nvarchar](256) NULL,
    [ConcurrencyStamp] [nvarchar](max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

CREATE TABLE [dbo].[AspNetUsers](
    [Id]                   [nvarchar](450)     NOT NULL,
    [FullName]             [nvarchar](max)      NULL,
    [IsActive]             [bit]               NOT NULL DEFAULT (1),
    [CreatedAt]            [datetime2](7)       NOT NULL DEFAULT (GETDATE()),
    [UserName]             [nvarchar](256)      NULL,
    [NormalizedUserName]   [nvarchar](256)      NULL,
    [Email]                [nvarchar](256)      NULL,
    [NormalizedEmail]      [nvarchar](256)      NULL,
    [EmailConfirmed]       [bit]               NOT NULL DEFAULT (0),
    [PasswordHash]         [nvarchar](max)      NULL,
    [SecurityStamp]        [nvarchar](max)      NULL,
    [ConcurrencyStamp]     [nvarchar](max)      NULL,
    [PhoneNumber]          [nvarchar](max)      NULL,
    [PhoneNumberConfirmed] [bit]               NOT NULL DEFAULT (0),
    [TwoFactorEnabled]     [bit]               NOT NULL DEFAULT (0),
    [LockoutEnd]           [datetimeoffset](7)  NULL,
    [LockoutEnabled]       [bit]               NOT NULL DEFAULT (1),
    [AccessFailedCount]    [int]               NOT NULL DEFAULT (0),
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

CREATE TABLE [dbo].[AspNetRoleClaims](
    [Id]         [int]           IDENTITY(1,1) NOT NULL,
    [RoleId]     [nvarchar](450) NOT NULL,
    [ClaimType]  [nvarchar](max) NULL,
    [ClaimValue] [nvarchar](max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

CREATE TABLE [dbo].[AspNetUserClaims](
    [Id]         [int]           IDENTITY(1,1) NOT NULL,
    [UserId]     [nvarchar](450) NOT NULL,
    [ClaimType]  [nvarchar](max) NULL,
    [ClaimValue] [nvarchar](max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

CREATE TABLE [dbo].[AspNetUserLogins](
    [LoginProvider]       [nvarchar](450) NOT NULL,
    [ProviderKey]         [nvarchar](450) NOT NULL,
    [ProviderDisplayName] [nvarchar](max) NULL,
    [UserId]              [nvarchar](450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins]
        PRIMARY KEY CLUSTERED ([LoginProvider] ASC, [ProviderKey] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

CREATE TABLE [dbo].[AspNetUserRoles](
    [UserId] [nvarchar](450) NOT NULL,
    [RoleId] [nvarchar](450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles]
        PRIMARY KEY CLUSTERED ([UserId] ASC, [RoleId] ASC)
) ON [PRIMARY]
GO

CREATE TABLE [dbo].[AspNetUserTokens](
    [UserId]        [nvarchar](450) NOT NULL,
    [LoginProvider] [nvarchar](450) NOT NULL,
    [Name]          [nvarchar](450) NOT NULL,
    [Value]         [nvarchar](max) NULL,
    CONSTRAINT [PK_AspNetUserTokens]
        PRIMARY KEY CLUSTERED ([UserId] ASC, [LoginProvider] ASC, [Name] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Permissions & Role Permissions
-- ----------------------------------------------------------
CREATE TABLE [dbo].[Permissions](
    [Id]          [int]           IDENTITY(1,1) NOT NULL,
    [Module]      [nvarchar](max) NOT NULL,
    [Action]      [nvarchar](max) NOT NULL,
    [Key]         [nvarchar](450) NOT NULL,
    [Description] [nvarchar](max) NULL,
    CONSTRAINT [PK_Permissions] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

CREATE TABLE [dbo].[RolePermissions](
    [Id]           [int]           IDENTITY(1,1) NOT NULL,
    [RoleId]       [nvarchar](450) NOT NULL,
    [PermissionId] [int]           NOT NULL,
    [CanView]      [bit]           NOT NULL DEFAULT (0),
    [CanCreate]    [bit]           NOT NULL DEFAULT (0),
    [CanEdit]      [bit]           NOT NULL DEFAULT (0),
    [CanDelete]    [bit]           NOT NULL DEFAULT (0),
    CONSTRAINT [PK_RolePermissions] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Companies
-- ----------------------------------------------------------
CREATE TABLE [dbo].[Companies](
    [Id]          [int]           IDENTITY(1,1) NOT NULL,
    [CompanyName] [nvarchar](450) NOT NULL,
    [Address]     [nvarchar](max) NULL,
    [NTN]         [nvarchar](max) NULL,
    [ProvinceId]  [int]           NULL,
    [Phone]       [nvarchar](max) NULL,
    [Email]       [nvarchar](max) NULL,
    [FbrPostUrl]  [nvarchar](max) NULL,
    [ApiToken]    [nvarchar](max) NULL,
    [LogoPath]    [nvarchar](max) NULL,
    [IsDefault]   [bit]           NOT NULL DEFAULT (0),
    [CreatedAt]   [datetime2](7)  NOT NULL,
    [CreatedBy]   [nvarchar](max) NULL,
    [UpdatedAt]   [datetime2](7)  NULL,
    [UpdatedBy]   [nvarchar](max) NULL,
    [IsDeleted]   [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]   [datetime2](7)  NULL,
    [DeletedBy]   [nvarchar](max) NULL,
    CONSTRAINT [PK_Companies] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- User Companies  (many-to-many)
-- ----------------------------------------------------------
CREATE TABLE [dbo].[UserCompanies](
    [UserId]    [nvarchar](450) NOT NULL,
    [CompanyId] [int]           NOT NULL,
    CONSTRAINT [PK_UserCompanies]
        PRIMARY KEY CLUSTERED ([UserId] ASC, [CompanyId] ASC)
) ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Chart Of Accounts
-- ----------------------------------------------------------
CREATE TABLE [dbo].[ChartOfAccounts](
    [Id]             [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId]      [int]           NOT NULL,
    [AccountNumber]  [nvarchar](450) NOT NULL,
    [AccountName]    [nvarchar](max) NOT NULL,
    [TypeId]         [int]           NULL,        -- FK → AccountTypes.TypeId
    [SubTypeId]      [int]           NULL,        -- FK → SubAccountTypes.SubTypeId
    [Description]    [nvarchar](max) NULL,
    [IsActive]       [bit]           NOT NULL DEFAULT (1),
    [OpeningBalance] [decimal](18,2) NOT NULL DEFAULT (0),
    [CreatedAt]      [datetime2](7)  NOT NULL,
    [CreatedBy]      [nvarchar](max) NULL,
    [UpdatedAt]      [datetime2](7)  NULL,
    [UpdatedBy]      [nvarchar](max) NULL,
    [IsDeleted]      [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]      [datetime2](7)  NULL,
    [DeletedBy]      [nvarchar](max) NULL,
    CONSTRAINT [PK_ChartOfAccounts] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Audit Logs
-- ----------------------------------------------------------
CREATE TABLE [dbo].[AuditLogs](
    [Id]        [bigint]        IDENTITY(1,1) NOT NULL,
    [UserId]    [nvarchar](max) NULL,
    [UserName]  [nvarchar](max) NULL,
    [Action]    [nvarchar](max) NOT NULL,
    [TableName] [nvarchar](max) NULL,
    [RecordId]  [nvarchar](max) NULL,
    [OldValue]  [nvarchar](max) NULL,
    [NewValue]  [nvarchar](max) NULL,
    [IPAddress] [nvarchar](max) NULL,
    [CreatedAt] [datetime2](7)  NOT NULL,
    [CompanyId] [int]           NULL,
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Tax Settings
-- ----------------------------------------------------------
CREATE TABLE [dbo].[TaxSettings](
    [Id]                       [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId]                [int]           NOT NULL,
    [GroupName]                [nvarchar](max) NOT NULL,
    [Description]              [nvarchar](max) NULL,
    [SalesTaxRate]             [decimal](18,2) NOT NULL DEFAULT (18),
    [UnregisteredSalesTaxRate] [decimal](18,2) NOT NULL DEFAULT (18),
    [IsActive]                 [bit]           NOT NULL DEFAULT (1),
    [CreatedAt]                [datetime2](7)  NOT NULL,
    [CreatedBy]                [nvarchar](max) NULL,
    [UpdatedAt]                [datetime2](7)  NULL,
    [UpdatedBy]                [nvarchar](max) NULL,
    [IsDeleted]                [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]                [datetime2](7)  NULL,
    [DeletedBy]                [nvarchar](max) NULL,
    CONSTRAINT [PK_TaxSettings] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Customers
-- ----------------------------------------------------------
CREATE TABLE [dbo].[Customers](
    [Id]             [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId]      [int]           NOT NULL,
    [BuyerId]        [nvarchar](450) NOT NULL,
    [BuyerName]      [nvarchar](max) NOT NULL,
    [OpeningBalance] [decimal](18,2) NOT NULL DEFAULT (0),
    [Address]        [nvarchar](max) NULL,
    [ProvinceId]     [int]           NULL,
    [ScenarioId]     [int]           NOT NULL,
    [Phone]          [nvarchar](max) NULL,
    [Mobile]         [nvarchar](max) NULL,
    [Email]          [nvarchar](max) NULL,
    [NTN]            [nvarchar](max) NULL,
    [CNIC]           [nvarchar](max) NULL,
    [STRN]           [nvarchar](max) NULL,
    [CustomerType]   [int]           NOT NULL DEFAULT (1),
    [InvoiceType]    [int]           NOT NULL DEFAULT (1),
    [IsActive]       [bit]           NOT NULL DEFAULT (1),
    [CreatedAt]      [datetime2](7)  NOT NULL,
    [CreatedBy]      [nvarchar](max) NULL,
    [UpdatedAt]      [datetime2](7)  NULL,
    [UpdatedBy]      [nvarchar](max) NULL,
    [IsDeleted]      [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]      [datetime2](7)  NULL,
    [DeletedBy]      [nvarchar](max) NULL,
    CONSTRAINT [PK_Customers] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Vendors
-- ----------------------------------------------------------
CREATE TABLE [dbo].[Vendors](
    [Id]                  [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId]           [int]           NOT NULL,
    [VendorCode]          [nvarchar](450) NOT NULL,
    [VendorName]          [nvarchar](max) NOT NULL,
    [OpeningBalance]      [decimal](18,2) NOT NULL DEFAULT (0),
    [Address]             [nvarchar](max) NULL,
    [ProvinceId]          [int]           NULL,
    [Phone]               [nvarchar](max) NULL,
    [Email]               [nvarchar](max) NULL,
    [NTN]                 [nvarchar](max) NULL,
    [DefaultSalesTaxRate] [decimal](18,2) NOT NULL DEFAULT (18),
    [IsActive]            [bit]           NOT NULL DEFAULT (1),
    [CreatedAt]           [datetime2](7)  NOT NULL,
    [CreatedBy]           [nvarchar](max) NULL,
    [UpdatedAt]           [datetime2](7)  NULL,
    [UpdatedBy]           [nvarchar](max) NULL,
    [IsDeleted]           [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]           [datetime2](7)  NULL,
    [DeletedBy]           [nvarchar](max) NULL,
    CONSTRAINT [PK_Vendors] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Item Categories
-- ----------------------------------------------------------
CREATE TABLE [dbo].[ItemCategories](
    [Id]          [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId]   [int]           NOT NULL,
    [Name]        [nvarchar](max) NOT NULL,
    [Description] [nvarchar](max) NULL,
    [CreatedAt]   [datetime2](7)  NOT NULL,
    [CreatedBy]   [nvarchar](max) NULL,
    [UpdatedAt]   [datetime2](7)  NULL,
    [UpdatedBy]   [nvarchar](max) NULL,
    [IsDeleted]   [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]   [datetime2](7)  NULL,
    [DeletedBy]   [nvarchar](max) NULL,
    CONSTRAINT [PK_ItemCategories] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Items
-- ----------------------------------------------------------
CREATE TABLE [dbo].[Items](
    [Id]              [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId]       [int]           NOT NULL,
    [ItemType]        [int]           NOT NULL DEFAULT (1),
    [ItemCode]        [nvarchar](450) NOT NULL,
    [ItemName]        [nvarchar](max) NOT NULL,
    [StackNo]         [nvarchar](max) NOT NULL,
    [LotNo]           [nvarchar](max) NOT NULL,
    [Description]     [nvarchar](max) NULL,
    [HSCode]          [nvarchar](max) NULL,
    [Barcode]         [nvarchar](max) NULL,
    [UnitOfMeasureId] [int]           NOT NULL,
    [ItemCategoryId]  [int]           NULL,
    [PurchaseRate]    [decimal](18,2) NOT NULL DEFAULT (0),
    [SaleRate]        [decimal](18,2) NOT NULL DEFAULT (0),
    [MinimumStock]    [decimal](18,2) NOT NULL DEFAULT (0),
    [ReorderLevel]    [decimal](18,2) NOT NULL DEFAULT (0),
    [CurrentStock]    [decimal](18,2) NOT NULL DEFAULT (0),
    [CostingMethod]   [int]           NOT NULL DEFAULT (1),
    [IsActive]        [bit]           NOT NULL DEFAULT (1),
    [CreatedAt]       [datetime2](7)  NOT NULL,
    [CreatedBy]       [nvarchar](max) NULL,
    [UpdatedAt]       [datetime2](7)  NULL,
    [UpdatedBy]       [nvarchar](max) NULL,
    [IsDeleted]       [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]       [datetime2](7)  NULL,
    [DeletedBy]       [nvarchar](max) NULL,
    CONSTRAINT [PK_Items] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Warehouses
-- ----------------------------------------------------------
CREATE TABLE [dbo].[Warehouses](
    [Id]        [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId] [int]           NOT NULL,
    [Code]      [nvarchar](max) NOT NULL,
    [Name]      [nvarchar](max) NOT NULL,
    [Address]   [nvarchar](max) NULL,
    [IsActive]  [bit]           NOT NULL DEFAULT (1),
    [CreatedAt] [datetime2](7)  NOT NULL,
    [CreatedBy] [nvarchar](max) NULL,
    [UpdatedAt] [datetime2](7)  NULL,
    [UpdatedBy] [nvarchar](max) NULL,
    [IsDeleted] [bit]           NOT NULL DEFAULT (0),
    [DeletedAt] [datetime2](7)  NULL,
    [DeletedBy] [nvarchar](max) NULL,
    CONSTRAINT [PK_Warehouses] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Inventory Transactions
-- ----------------------------------------------------------
CREATE TABLE [dbo].[InventoryTransactions](
    [Id]              [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId]       [int]           NOT NULL,
    [ItemId]          [int]           NOT NULL,
    [WarehouseId]     [int]           NOT NULL,
    [TransactionType] [int]           NOT NULL,
    [StackNo]         [nvarchar](max) NULL,
    [LotNo]           [nvarchar](max) NULL,
    [Quantity]        [decimal](18,2) NOT NULL,
    [UnitCost]        [decimal](18,2) NOT NULL DEFAULT (0),
    [TotalCost]       [decimal](18,2) NOT NULL DEFAULT (0),
    [TransactionDate] [datetime2](7)  NOT NULL,
    [ReferenceNo]     [nvarchar](max) NULL,
    [Notes]           [nvarchar](max) NULL,
    [CreatedAt]       [datetime2](7)  NOT NULL,
    [CreatedBy]       [nvarchar](max) NULL,
    [UpdatedAt]       [datetime2](7)  NULL,
    [UpdatedBy]       [nvarchar](max) NULL,
    [IsDeleted]       [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]       [datetime2](7)  NULL,
    [DeletedBy]       [nvarchar](max) NULL,
    CONSTRAINT [PK_InventoryTransactions] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Banks
-- ----------------------------------------------------------
CREATE TABLE [dbo].[Banks](
    [Id]               [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId]        [int]           NOT NULL,
    [BankName]         [nvarchar](max) NOT NULL,
    [AccountTitle]     [nvarchar](max) NOT NULL,
    [AccountNumber]    [nvarchar](max) NOT NULL,
    [IBAN]             [nvarchar](max) NULL,
    [ChartOfAccountId] [int]           NULL,
    [OpeningBalance]   [decimal](18,2) NOT NULL DEFAULT (0),
    [CurrentBalance]   [decimal](18,2) NOT NULL DEFAULT (0),
    [IsActive]         [bit]           NOT NULL DEFAULT (1),
    [CreatedAt]        [datetime2](7)  NOT NULL,
    [CreatedBy]        [nvarchar](max) NULL,
    [UpdatedAt]        [datetime2](7)  NULL,
    [UpdatedBy]        [nvarchar](max) NULL,
    [IsDeleted]        [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]        [datetime2](7)  NULL,
    [DeletedBy]        [nvarchar](max) NULL,
    CONSTRAINT [PK_Banks] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Bank Transactions
-- ----------------------------------------------------------
CREATE TABLE [dbo].[BankTransactions](
    [Id]               [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId]        [int]           NOT NULL,
    [BankId]           [int]           NOT NULL,
    [TransactionType]  [int]           NOT NULL,
    [TransferToBankId] [int]           NULL,
    [TransactionDate]  [datetime2](7)  NOT NULL,
    [ChequeNumber]     [nvarchar](max) NULL,
    [ChequeDate]       [datetime2](7)  NULL,
    [Amount]           [decimal](18,2) NOT NULL,
    [Description]      [nvarchar](max) NULL,
    [IsReconciled]     [bit]           NOT NULL DEFAULT (0),
    [CreatedAt]        [datetime2](7)  NOT NULL,
    [CreatedBy]        [nvarchar](max) NULL,
    [UpdatedAt]        [datetime2](7)  NULL,
    [UpdatedBy]        [nvarchar](max) NULL,
    [IsDeleted]        [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]        [datetime2](7)  NULL,
    [DeletedBy]        [nvarchar](max) NULL,
    CONSTRAINT [PK_BankTransactions] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Bank Reconciliations
-- ----------------------------------------------------------
CREATE TABLE [dbo].[BankReconciliations](
    [Id]               [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId]        [int]           NOT NULL,
    [BankId]           [int]           NOT NULL,
    [StatementDate]    [datetime2](7)  NOT NULL,
    [StatementBalance] [decimal](18,2) NOT NULL,
    [BookBalance]      [decimal](18,2) NOT NULL,
    [IsCompleted]      [bit]           NOT NULL DEFAULT (0),
    [CreatedAt]        [datetime2](7)  NOT NULL,
    [CreatedBy]        [nvarchar](max) NULL,
    [UpdatedAt]        [datetime2](7)  NULL,
    [UpdatedBy]        [nvarchar](max) NULL,
    [IsDeleted]        [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]        [datetime2](7)  NULL,
    [DeletedBy]        [nvarchar](max) NULL,
    CONSTRAINT [PK_BankReconciliations] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Journal Entries
-- ----------------------------------------------------------
CREATE TABLE [dbo].[JournalEntries](
    [Id]            [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId]     [int]           NOT NULL,
    [EntryNumber]   [nvarchar](max) NOT NULL,
    [EntryDate]     [datetime2](7)  NOT NULL,
    [Description]   [nvarchar](max) NULL,
    [ReferenceType] [nvarchar](100) NULL,   -- e.g. SalesInvoice, VendorBill (indexable)
    [ReferenceId]   [int]           NULL,
    [Status]        [int]           NOT NULL DEFAULT (1),
    [CreatedAt]     [datetime2](7)  NOT NULL,
    [CreatedBy]     [nvarchar](max) NULL,
    [UpdatedAt]     [datetime2](7)  NULL,
    [UpdatedBy]     [nvarchar](max) NULL,
    [IsDeleted]     [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]     [datetime2](7)  NULL,
    [DeletedBy]     [nvarchar](max) NULL,
    CONSTRAINT [PK_JournalEntries] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Journal Entry Lines
-- ----------------------------------------------------------
CREATE TABLE [dbo].[JournalEntryLines](
    [Id]               [int]           IDENTITY(1,1) NOT NULL,
    [JournalEntryId]   [int]           NOT NULL,
    [ChartOfAccountId] [int]           NOT NULL,
    [Debit]            [decimal](18,2) NOT NULL DEFAULT (0),
    [Credit]           [decimal](18,2) NOT NULL DEFAULT (0),
    [Memo]             [nvarchar](max) NULL,
    CONSTRAINT [PK_JournalEntryLines] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Sales Invoices
-- ----------------------------------------------------------
CREATE TABLE [dbo].[SalesInvoices](
    [Id]               [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId]        [int]           NOT NULL,
    [InvoiceNumber]    [nvarchar](450) NOT NULL,
    [CustomerId]       [int]           NOT NULL,
    [BuyerAddress]     [nvarchar](max) NULL,
    [ProvinceId]       [int]           NULL,
    [BuyerNTN]         [nvarchar](max) NULL,
    [BuyerCNIC]        [nvarchar](max) NULL,
    [InvoiceDate]      [datetime2](7)  NOT NULL,
    [InvoiceType]      [int]           NOT NULL DEFAULT (1),
    [ScenarioId]       [int]           NULL,
    [SubTotal]         [decimal](18,2) NOT NULL DEFAULT (0),
    [DiscountAmount]   [decimal](18,2) NOT NULL DEFAULT (0),
    [TaxAmount]        [decimal](18,2) NOT NULL DEFAULT (0),
    [FurtherTax]       [decimal](18,2) NOT NULL DEFAULT (0),
    [FED]              [decimal](18,2) NOT NULL DEFAULT (0),
    [ExtraTax]         [decimal](18,2) NOT NULL DEFAULT (0),
    [WithholdingTax]   [decimal](18,2) NOT NULL DEFAULT (0),
    [NetTotal]         [decimal](18,2) NOT NULL DEFAULT (0),
    [Status]           [int]           NOT NULL DEFAULT (1),
    [JournalEntryId]   [int]           NULL,
    [FbrInvoiceNumber] [nvarchar](max) NULL,
    [FbrResponseJson]  [nvarchar](max) NULL,
    [FbrSubmittedAt]   [datetime2](7)  NULL,
    [CreatedAt]        [datetime2](7)  NOT NULL,
    [CreatedBy]        [nvarchar](max) NULL,
    [UpdatedAt]        [datetime2](7)  NULL,
    [UpdatedBy]        [nvarchar](max) NULL,
    [IsDeleted]        [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]        [datetime2](7)  NULL,
    [DeletedBy]        [nvarchar](max) NULL,
    CONSTRAINT [PK_SalesInvoices] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Sales Invoice Lines
-- ----------------------------------------------------------
CREATE TABLE [dbo].[SalesInvoiceLines](
    [Id]                 [int]           IDENTITY(1,1) NOT NULL,
    [SalesInvoiceId]     [int]           NOT NULL,
    [ItemId]             [int]           NOT NULL,
    [HSCode]             [nvarchar](max) NULL,
    [ProductDescription] [nvarchar](max) NULL,
    [Unit]               [nvarchar](max) NULL,
    [Quantity]           [decimal](18,2) NOT NULL DEFAULT (0),
    [Cartons]            [decimal](18,2) NOT NULL DEFAULT (0),
    [Price]              [decimal](18,2) NOT NULL DEFAULT (0),
    [TaxRate]            [decimal](18,2) NOT NULL DEFAULT (0),
    [TaxAmount]          [decimal](18,2) NOT NULL DEFAULT (0),
    [Discount]           [decimal](18,2) NOT NULL DEFAULT (0),
    [LineTotal]          [decimal](18,2) NOT NULL DEFAULT (0),
    CONSTRAINT [PK_SalesInvoiceLines] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Vendor Bills
-- ----------------------------------------------------------
CREATE TABLE [dbo].[VendorBills](
    [Id]             [int]           IDENTITY(1,1) NOT NULL,
    [CompanyId]      [int]           NOT NULL,
    [VendorId]       [int]           NOT NULL,
    [BillNumber]     [nvarchar](max) NOT NULL,
    [RefNo]          [nvarchar](max) NULL,
    [BillDate]       [datetime2](7)  NOT NULL,
    [TotalQuantity]  [decimal](18,2) NOT NULL DEFAULT (0),
    [TotalCartons]   [decimal](18,2) NOT NULL DEFAULT (0),
    [TaxAmount]      [decimal](18,2) NOT NULL DEFAULT (0),
    [NetAmount]      [decimal](18,2) NOT NULL DEFAULT (0),
    [Status]         [int]           NOT NULL DEFAULT (1),
    [JournalEntryId] [int]           NULL,
    [CreatedAt]      [datetime2](7)  NOT NULL,
    [CreatedBy]      [nvarchar](max) NULL,
    [UpdatedAt]      [datetime2](7)  NULL,
    [UpdatedBy]      [nvarchar](max) NULL,
    [IsDeleted]      [bit]           NOT NULL DEFAULT (0),
    [DeletedAt]      [datetime2](7)  NULL,
    [DeletedBy]      [nvarchar](max) NULL,
    CONSTRAINT [PK_VendorBills] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ----------------------------------------------------------
-- Vendor Bill Lines
-- ----------------------------------------------------------
CREATE TABLE [dbo].[VendorBillLines](
    [Id]           [int]           IDENTITY(1,1) NOT NULL,
    [VendorBillId] [int]           NOT NULL,
    [ItemId]       [int]           NULL,
    [Description]  [nvarchar](max) NULL,
    [Quantity]     [decimal](18,2) NOT NULL DEFAULT (0),
    [Cartons]      [decimal](18,2) NOT NULL DEFAULT (0),
    [Rate]         [decimal](18,2) NOT NULL DEFAULT (0),
    [Amount]       [decimal](18,2) NOT NULL DEFAULT (0),
    CONSTRAINT [PK_VendorBillLines] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [CK_VendorBillLines_ItemOrDesc]
        CHECK ([ItemId] IS NOT NULL OR ([Description] IS NOT NULL AND LEN([Description]) > 0))
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- ============================================================
-- FOREIGN KEY CONSTRAINTS
-- ============================================================

-- ---------- ASP.NET Identity ----------
ALTER TABLE [dbo].[AspNetRoleClaims] WITH CHECK ADD CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId]
    FOREIGN KEY([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]) ON DELETE CASCADE
GO
ALTER TABLE [dbo].[AspNetUserClaims] WITH CHECK ADD CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId]
    FOREIGN KEY([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
GO
ALTER TABLE [dbo].[AspNetUserLogins] WITH CHECK ADD CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId]
    FOREIGN KEY([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
GO
ALTER TABLE [dbo].[AspNetUserRoles] WITH CHECK ADD CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId]
    FOREIGN KEY([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]) ON DELETE CASCADE
GO
ALTER TABLE [dbo].[AspNetUserRoles] WITH CHECK ADD CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId]
    FOREIGN KEY([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
GO
ALTER TABLE [dbo].[AspNetUserTokens] WITH CHECK ADD CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId]
    FOREIGN KEY([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
GO

-- ---------- Permissions ----------
ALTER TABLE [dbo].[RolePermissions] WITH CHECK ADD CONSTRAINT [FK_RolePermissions_Permissions_PermissionId]
    FOREIGN KEY([PermissionId]) REFERENCES [dbo].[Permissions] ([Id])
GO
ALTER TABLE [dbo].[RolePermissions] WITH CHECK ADD CONSTRAINT [FK_RolePermissions_AspNetRoles_RoleId]
    FOREIGN KEY([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id])
GO

-- ---------- Companies ----------
ALTER TABLE [dbo].[Companies] WITH CHECK ADD CONSTRAINT [FK_Companies_Provinces_ProvinceId]
    FOREIGN KEY([ProvinceId]) REFERENCES [dbo].[Provinces] ([Id])
GO
ALTER TABLE [dbo].[UserCompanies] WITH CHECK ADD CONSTRAINT [FK_UserCompanies_AspNetUsers_UserId]
    FOREIGN KEY([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id])
GO
ALTER TABLE [dbo].[UserCompanies] WITH CHECK ADD CONSTRAINT [FK_UserCompanies_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO

-- ---------- Chart Of Accounts ----------
ALTER TABLE [dbo].[ChartOfAccounts] WITH CHECK ADD CONSTRAINT [FK_ChartOfAccounts_AccountTypes_TypeId]
    FOREIGN KEY([TypeId]) REFERENCES [dbo].[AccountTypes] ([TypeId])
GO
ALTER TABLE [dbo].[ChartOfAccounts] WITH CHECK ADD CONSTRAINT [FK_ChartOfAccounts_SubAccountTypes_SubTypeId]
    FOREIGN KEY([SubTypeId]) REFERENCES [dbo].[SubAccountTypes] ([SubTypeId])
GO
ALTER TABLE [dbo].[ChartOfAccounts] WITH CHECK ADD CONSTRAINT [FK_ChartOfAccounts_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO

-- ---------- Tax Settings ----------
ALTER TABLE [dbo].[TaxSettings] WITH CHECK ADD CONSTRAINT [FK_TaxSettings_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO

-- ---------- Customers ----------
ALTER TABLE [dbo].[Customers] WITH CHECK ADD CONSTRAINT [FK_Customers_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO
ALTER TABLE [dbo].[Customers] WITH CHECK ADD CONSTRAINT [FK_Customers_Provinces_ProvinceId]
    FOREIGN KEY([ProvinceId]) REFERENCES [dbo].[Provinces] ([Id])
GO
ALTER TABLE [dbo].[Customers] WITH CHECK ADD CONSTRAINT [FK_Customers_ScenarioTypes_ScenarioId]
    FOREIGN KEY([ScenarioId]) REFERENCES [dbo].[ScenarioTypes] ([ScenarioId])
GO

-- ---------- Vendors ----------
ALTER TABLE [dbo].[Vendors] WITH CHECK ADD CONSTRAINT [FK_Vendors_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO
ALTER TABLE [dbo].[Vendors] WITH CHECK ADD CONSTRAINT [FK_Vendors_Provinces_ProvinceId]
    FOREIGN KEY([ProvinceId]) REFERENCES [dbo].[Provinces] ([Id])
GO

-- ---------- Item Categories ----------
ALTER TABLE [dbo].[ItemCategories] WITH CHECK ADD CONSTRAINT [FK_ItemCategories_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO

-- ---------- Items ----------
ALTER TABLE [dbo].[Items] WITH CHECK ADD CONSTRAINT [FK_Items_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO
ALTER TABLE [dbo].[Items] WITH CHECK ADD CONSTRAINT [FK_Items_ItemCategories_ItemCategoryId]
    FOREIGN KEY([ItemCategoryId]) REFERENCES [dbo].[ItemCategories] ([Id])
GO
ALTER TABLE [dbo].[Items] WITH CHECK ADD CONSTRAINT [FK_Items_UnitsOfMeasure_UnitOfMeasureId]
    FOREIGN KEY([UnitOfMeasureId]) REFERENCES [dbo].[UnitsOfMeasure] ([Id])
GO

-- ---------- Warehouses ----------
ALTER TABLE [dbo].[Warehouses] WITH CHECK ADD CONSTRAINT [FK_Warehouses_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO

-- ---------- Inventory Transactions ----------
ALTER TABLE [dbo].[InventoryTransactions] WITH CHECK ADD CONSTRAINT [FK_InventoryTransactions_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO
ALTER TABLE [dbo].[InventoryTransactions] WITH CHECK ADD CONSTRAINT [FK_InventoryTransactions_Items_ItemId]
    FOREIGN KEY([ItemId]) REFERENCES [dbo].[Items] ([Id])
GO
ALTER TABLE [dbo].[InventoryTransactions] WITH CHECK ADD CONSTRAINT [FK_InventoryTransactions_Warehouses_WarehouseId]
    FOREIGN KEY([WarehouseId]) REFERENCES [dbo].[Warehouses] ([Id])
GO

-- ---------- Banks ----------
ALTER TABLE [dbo].[Banks] WITH CHECK ADD CONSTRAINT [FK_Banks_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO
ALTER TABLE [dbo].[Banks] WITH CHECK ADD CONSTRAINT [FK_Banks_ChartOfAccounts_ChartOfAccountId]
    FOREIGN KEY([ChartOfAccountId]) REFERENCES [dbo].[ChartOfAccounts] ([Id])
GO

-- ---------- Bank Transactions ----------
ALTER TABLE [dbo].[BankTransactions] WITH CHECK ADD CONSTRAINT [FK_BankTransactions_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO
ALTER TABLE [dbo].[BankTransactions] WITH CHECK ADD CONSTRAINT [FK_BankTransactions_Banks_BankId]
    FOREIGN KEY([BankId]) REFERENCES [dbo].[Banks] ([Id])
GO
-- v5 fix: missing FK for inter-bank transfers
ALTER TABLE [dbo].[BankTransactions] WITH CHECK ADD CONSTRAINT [FK_BankTransactions_Banks_TransferToBankId]
    FOREIGN KEY([TransferToBankId]) REFERENCES [dbo].[Banks] ([Id])
GO

-- ---------- Bank Reconciliations ----------
ALTER TABLE [dbo].[BankReconciliations] WITH CHECK ADD CONSTRAINT [FK_BankReconciliations_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO
ALTER TABLE [dbo].[BankReconciliations] WITH CHECK ADD CONSTRAINT [FK_BankReconciliations_Banks_BankId]
    FOREIGN KEY([BankId]) REFERENCES [dbo].[Banks] ([Id])
GO

-- ---------- Audit Logs ----------
ALTER TABLE [dbo].[AuditLogs] WITH CHECK ADD CONSTRAINT [FK_AuditLogs_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO

-- ---------- Journal Entries ----------
ALTER TABLE [dbo].[JournalEntries] WITH CHECK ADD CONSTRAINT [FK_JournalEntries_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO
ALTER TABLE [dbo].[JournalEntryLines] WITH CHECK ADD CONSTRAINT [FK_JournalEntryLines_JournalEntries_JournalEntryId]
    FOREIGN KEY([JournalEntryId]) REFERENCES [dbo].[JournalEntries] ([Id])
GO
ALTER TABLE [dbo].[JournalEntryLines] WITH CHECK ADD CONSTRAINT [FK_JournalEntryLines_ChartOfAccounts_ChartOfAccountId]
    FOREIGN KEY([ChartOfAccountId]) REFERENCES [dbo].[ChartOfAccounts] ([Id])
GO

-- ---------- Sales Invoices ----------
ALTER TABLE [dbo].[SalesInvoices] WITH CHECK ADD CONSTRAINT [FK_SalesInvoices_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO
ALTER TABLE [dbo].[SalesInvoices] WITH CHECK ADD CONSTRAINT [FK_SalesInvoices_Customers_CustomerId]
    FOREIGN KEY([CustomerId]) REFERENCES [dbo].[Customers] ([Id])
GO
ALTER TABLE [dbo].[SalesInvoices] WITH CHECK ADD CONSTRAINT [FK_SalesInvoices_JournalEntries_JournalEntryId]
    FOREIGN KEY([JournalEntryId]) REFERENCES [dbo].[JournalEntries] ([Id])
GO
ALTER TABLE [dbo].[SalesInvoices] WITH CHECK ADD CONSTRAINT [FK_SalesInvoices_ScenarioTypes_ScenarioId]
    FOREIGN KEY([ScenarioId]) REFERENCES [dbo].[ScenarioTypes] ([ScenarioId])
GO
-- v5 fix: missing FK for buyer province on invoice
ALTER TABLE [dbo].[SalesInvoices] WITH CHECK ADD CONSTRAINT [FK_SalesInvoices_Provinces_ProvinceId]
    FOREIGN KEY([ProvinceId]) REFERENCES [dbo].[Provinces] ([Id])
GO
ALTER TABLE [dbo].[SalesInvoiceLines] WITH CHECK ADD CONSTRAINT [FK_SalesInvoiceLines_SalesInvoices_SalesInvoiceId]
    FOREIGN KEY([SalesInvoiceId]) REFERENCES [dbo].[SalesInvoices] ([Id])
GO
ALTER TABLE [dbo].[SalesInvoiceLines] WITH CHECK ADD CONSTRAINT [FK_SalesInvoiceLines_Items_ItemId]
    FOREIGN KEY([ItemId]) REFERENCES [dbo].[Items] ([Id])
GO

-- ---------- Vendor Bills ----------
ALTER TABLE [dbo].[VendorBills] WITH CHECK ADD CONSTRAINT [FK_VendorBills_Companies_CompanyId]
    FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
GO
ALTER TABLE [dbo].[VendorBills] WITH CHECK ADD CONSTRAINT [FK_VendorBills_Vendors_VendorId]
    FOREIGN KEY([VendorId]) REFERENCES [dbo].[Vendors] ([Id])
GO
ALTER TABLE [dbo].[VendorBills] WITH CHECK ADD CONSTRAINT [FK_VendorBills_JournalEntries_JournalEntryId]
    FOREIGN KEY([JournalEntryId]) REFERENCES [dbo].[JournalEntries] ([Id])
GO
ALTER TABLE [dbo].[VendorBillLines] WITH CHECK ADD CONSTRAINT [FK_VendorBillLines_VendorBills_VendorBillId]
    FOREIGN KEY([VendorBillId]) REFERENCES [dbo].[VendorBills] ([Id])
GO
ALTER TABLE [dbo].[VendorBillLines] WITH CHECK ADD CONSTRAINT [FK_VendorBillLines_Items_ItemId]
    FOREIGN KEY([ItemId]) REFERENCES [dbo].[Items] ([Id])
GO

-- ============================================================
-- PERFORMANCE INDEXES
-- ============================================================

CREATE INDEX IX_SalesInvoices_CompanyId_Date   ON [dbo].[SalesInvoices]([CompanyId],[InvoiceDate])
CREATE INDEX IX_SalesInvoices_CustomerId       ON [dbo].[SalesInvoices]([CustomerId])
CREATE INDEX IX_SalesInvoices_ScenarioId       ON [dbo].[SalesInvoices]([ScenarioId])
CREATE INDEX IX_SalesInvoices_ProvinceId       ON [dbo].[SalesInvoices]([ProvinceId])
CREATE INDEX IX_SalesInvoiceLines_InvoiceId    ON [dbo].[SalesInvoiceLines]([SalesInvoiceId])
CREATE INDEX IX_VendorBills_CompanyId_VendorId ON [dbo].[VendorBills]([CompanyId],[VendorId])
CREATE INDEX IX_VendorBillLines_BillId         ON [dbo].[VendorBillLines]([VendorBillId])
CREATE INDEX IX_InventoryTx_ItemId_Date        ON [dbo].[InventoryTransactions]([ItemId],[TransactionDate])
CREATE INDEX IX_JournalEntries_CompanyId_Ref   ON [dbo].[JournalEntries]([CompanyId],[ReferenceType],[ReferenceId])
CREATE INDEX IX_JournalEntryLines_EntryId      ON [dbo].[JournalEntryLines]([JournalEntryId])
CREATE INDEX IX_BankTransactions_BankId_Date   ON [dbo].[BankTransactions]([BankId],[TransactionDate])
CREATE INDEX IX_BankTransactions_TransferTo    ON [dbo].[BankTransactions]([TransferToBankId])
CREATE INDEX IX_AuditLogs_CompanyId_CreatedAt  ON [dbo].[AuditLogs]([CompanyId],[CreatedAt])
CREATE INDEX IX_ChartOfAccounts_CompanyId      ON [dbo].[ChartOfAccounts]([CompanyId])
CREATE INDEX IX_ChartOfAccounts_TypeId         ON [dbo].[ChartOfAccounts]([TypeId])
CREATE INDEX IX_ChartOfAccounts_SubTypeId      ON [dbo].[ChartOfAccounts]([SubTypeId])
CREATE INDEX IX_Customers_CompanyId            ON [dbo].[Customers]([CompanyId])
CREATE INDEX IX_Customers_ScenarioId           ON [dbo].[Customers]([ScenarioId])
CREATE INDEX IX_Vendors_CompanyId              ON [dbo].[Vendors]([CompanyId])
CREATE INDEX IX_Items_CompanyId                ON [dbo].[Items]([CompanyId])
CREATE INDEX IX_SubAccountTypes_TypeId         ON [dbo].[SubAccountTypes]([TypeId])
GO

-- ============================================================
-- ASP.NET IDENTITY INDEXES  (required for login/role lookups)
-- ============================================================

CREATE UNIQUE INDEX [RoleNameIndex] ON [dbo].[AspNetRoles]([NormalizedName])
    WHERE [NormalizedName] IS NOT NULL
GO
CREATE INDEX [EmailIndex] ON [dbo].[AspNetUsers]([NormalizedEmail])
GO
CREATE UNIQUE INDEX [UserNameIndex] ON [dbo].[AspNetUsers]([NormalizedUserName])
    WHERE [NormalizedUserName] IS NOT NULL
GO
CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [dbo].[AspNetRoleClaims]([RoleId])
GO
CREATE INDEX [IX_AspNetUserClaims_UserId] ON [dbo].[AspNetUserClaims]([UserId])
GO
CREATE INDEX [IX_AspNetUserLogins_UserId] ON [dbo].[AspNetUserLogins]([UserId])
GO
CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [dbo].[AspNetUserRoles]([RoleId])
GO

-- ============================================================
-- UNIQUE CONSTRAINTS  (filtered — exclude soft-deleted rows)
-- ============================================================

CREATE UNIQUE INDEX UX_SalesInvoices_Number ON [dbo].[SalesInvoices]([CompanyId],[InvoiceNumber]) WHERE [IsDeleted]=0
CREATE UNIQUE INDEX UX_Items_Code           ON [dbo].[Items]([CompanyId],[ItemCode])               WHERE [IsDeleted]=0
CREATE UNIQUE INDEX UX_Vendors_Code         ON [dbo].[Vendors]([CompanyId],[VendorCode])           WHERE [IsDeleted]=0
CREATE UNIQUE INDEX UX_Customers_BuyerId    ON [dbo].[Customers]([CompanyId],[BuyerId])            WHERE [IsDeleted]=0
CREATE UNIQUE INDEX UX_Permissions_Key      ON [dbo].[Permissions]([Key])
CREATE UNIQUE INDEX UX_RolePermissions      ON [dbo].[RolePermissions]([RoleId],[PermissionId])
CREATE UNIQUE INDEX UX_AccountTypes_Code    ON [dbo].[AccountTypes]([TypeCode])
CREATE UNIQUE INDEX UX_ScenarioTypes_Type   ON [dbo].[ScenarioTypes]([ScenarioType])
CREATE UNIQUE INDEX UX_SubAccountTypes_Code ON [dbo].[SubAccountTypes]([TypeId],[SubTypeCode])
GO

-- ============================================================
-- SEED DATA
-- ============================================================

-- ---------- Provinces (global) ----------
UPDATE [dbo].[SalesInvoices] SET [ProvinceId] = NULL WHERE [ProvinceId] IS NOT NULL
GO
UPDATE [dbo].[Customers]     SET [ProvinceId] = NULL WHERE [ProvinceId] IS NOT NULL
GO
UPDATE [dbo].[Vendors]       SET [ProvinceId] = NULL WHERE [ProvinceId] IS NOT NULL
GO
UPDATE [dbo].[Companies]     SET [ProvinceId] = NULL WHERE [ProvinceId] IS NOT NULL
GO

ALTER TABLE [dbo].[Companies]     NOCHECK CONSTRAINT [FK_Companies_Provinces_ProvinceId]
GO
ALTER TABLE [dbo].[Customers]     NOCHECK CONSTRAINT [FK_Customers_Provinces_ProvinceId]
GO
ALTER TABLE [dbo].[Vendors]       NOCHECK CONSTRAINT [FK_Vendors_Provinces_ProvinceId]
GO
ALTER TABLE [dbo].[SalesInvoices] NOCHECK CONSTRAINT [FK_SalesInvoices_Provinces_ProvinceId]
GO

DELETE FROM [dbo].[Provinces]
GO

DBCC CHECKIDENT ('[dbo].[Provinces]', RESEED, 0)
GO

SET IDENTITY_INSERT [dbo].[Provinces] ON
GO

INSERT INTO [dbo].[Provinces] ([Id], [Name], [Code]) VALUES
    (1, N'PUNJAB',                  N'PB'),
    (2, N'SINDH',                    N'SD'),
    (3, N'KHYBER PAKHTUNKHWA',       N'KP'),
    (4, N'BALOCHISTAN',              N'BC'),
    (5, N'CAPITAL TERRITORY',        N'ICT'),
    (6, N'AZAD JAMMU AND KASHMIR',   N'AJK'),
    (7, N'GILGIT BALTISTAN',         N'GB'),
    (8, N'FATA/PATA',                N'FATA')
GO

SET IDENTITY_INSERT [dbo].[Provinces] OFF
GO

ALTER TABLE [dbo].[Companies]     WITH CHECK CHECK CONSTRAINT [FK_Companies_Provinces_ProvinceId]
GO
ALTER TABLE [dbo].[Customers]     WITH CHECK CHECK CONSTRAINT [FK_Customers_Provinces_ProvinceId]
GO
ALTER TABLE [dbo].[Vendors]       WITH CHECK CHECK CONSTRAINT [FK_Vendors_Provinces_ProvinceId]
GO
ALTER TABLE [dbo].[SalesInvoices] WITH CHECK CHECK CONSTRAINT [FK_SalesInvoices_Provinces_ProvinceId]
GO

-- ---------- Units Of Measure (global) ----------
ALTER TABLE [dbo].[Items] NOCHECK CONSTRAINT [FK_Items_UnitsOfMeasure_UnitOfMeasureId]
GO

UPDATE [dbo].[Items]
SET [UnitOfMeasureId] = 1
WHERE [UnitOfMeasureId] NOT IN (1, 2, 3, 4, 5, 6)
   OR [UnitOfMeasureId] IS NULL
GO

DELETE FROM [dbo].[UnitsOfMeasure]
GO

DBCC CHECKIDENT ('[dbo].[UnitsOfMeasure]', RESEED, 0)
GO

SET IDENTITY_INSERT [dbo].[UnitsOfMeasure] ON
GO

INSERT INTO [dbo].[UnitsOfMeasure] ([Id], [Name], [Symbol]) VALUES
    (1, N'Kilogram',   N'KG'),
    (2, N'Pound',      N'LB'),
    (3, N'Per Piece',  N'PCS'),
    (4, N'Carton',     N'CTN'),
    (5, N'Litre',      N'LTR'),
    (6, N'Meter',      N'MTR')
GO

SET IDENTITY_INSERT [dbo].[UnitsOfMeasure] OFF
GO

ALTER TABLE [dbo].[Items] WITH CHECK CHECK CONSTRAINT [FK_Items_UnitsOfMeasure_UnitOfMeasureId]
GO

-- ---------- Account Types (global) ----------
SET IDENTITY_INSERT [dbo].[AccountTypes] ON
GO

INSERT INTO [dbo].[AccountTypes] ([TypeId], [TypeCode], [TypeName]) VALUES
    (1, N'ASSET',     N'Assets'),
    (2, N'LIABILITY', N'Liabilities'),
    (3, N'EQUITY',    N'Equity'),
    (4, N'REVENUE',   N'Revenue'),
    (5, N'COGS',      N'Cost of Goods Sold'),
    (6, N'EXPENSE',   N'Expenses')
GO

SET IDENTITY_INSERT [dbo].[AccountTypes] OFF
GO

-- ---------- Sub Account Types (global) ----------
SET IDENTITY_INSERT [dbo].[SubAccountTypes] ON
GO

INSERT INTO [dbo].[SubAccountTypes] ([SubTypeId], [TypeId], [SubTypeCode], [SubTypeName]) VALUES
    -- Assets (TypeId = 1)
    ( 1, 1, N'CASH',         N'Cash & Bank'),
    ( 2, 1, N'AR',           N'Accounts Receivable'),
    ( 3, 1, N'INVENTORY',    N'Inventory'),
    ( 4, 1, N'PREPAID',      N'Prepaid Expenses'),
    ( 5, 1, N'FIXED',        N'Fixed Assets'),
    ( 6, 1, N'INPUT_TAX',    N'Input Tax Recoverable'),
    ( 7, 1, N'OTHER_ASSET',  N'Other Assets'),

    -- Liabilities (TypeId = 2)
    ( 8,  2, N'AP',           N'Accounts Payable'),
    ( 9,  2, N'ACCRUED',      N'Accrued Liabilities'),
    (10,  2, N'TAX_PAYABLE',  N'Tax Payable'),
    (11,  2, N'LOAN_ST',      N'Short-term Loans'),
    (12,  2, N'LOAN_LT',      N'Long-term Loans'),
    (13,  2, N'OTHER_LIAB',   N'Other Liabilities'),

    -- Equity (TypeId = 3)
    (14, 3, N'CAPITAL',    N'Owner''s Capital'),
    (15, 3, N'RETAINED',   N'Retained Earnings'),
    (16, 3, N'DRAWINGS',   N'Owner''s Drawings'),
    (17, 3, N'RESERVES',   N'Reserves'),

    -- Revenue (TypeId = 4)
    (18, 4, N'SALES',          N'Sales Revenue'),
    (19, 4, N'SALES_RETURN',   N'Sales Returns'),
    (20, 4, N'OTHER_INCOME',   N'Other Income'),
    (21, 4, N'DISCOUNT_GIVEN', N'Discount Allowed'),

    -- Cost of Goods Sold (TypeId = 5)
    (22, 5, N'PURCHASE',        N'Purchases'),
    (23, 5, N'DIRECT_LABOR',    N'Direct Labor'),
    (24, 5, N'DIRECT_OH',       N'Direct Overhead'),
    (25, 5, N'FREIGHT_IN',      N'Freight In'),
    (26, 5, N'PURCHASE_RETURN', N'Purchase Returns'),
    (27, 5, N'INV_ADJ',         N'Inventory Adjustments'),

    -- Expenses (TypeId = 6)
    (28, 6, N'ADMIN',        N'Administrative Expenses'),
    (29, 6, N'SELLING',      N'Selling & Marketing'),
    (30, 6, N'PAYROLL',      N'Payroll & Benefits'),
    (31, 6, N'RENT',         N'Rent & Utilities'),
    (32, 6, N'DEPRECIATION', N'Depreciation'),
    (33, 6, N'FINANCE',      N'Finance Costs'),
    (34, 6, N'TAX_EXP',      N'Tax Expense'),
    (35, 6, N'OTHER_EXP',    N'Other Expenses')
GO

SET IDENTITY_INSERT [dbo].[SubAccountTypes] OFF
GO

-- ---------- Scenario Types (required before inserting Customers) ----------
-- Remove existing rows, then reseed (handles re-runs on dev/test databases)
UPDATE [dbo].[SalesInvoices] SET [ScenarioId] = NULL
WHERE [ScenarioId] IS NOT NULL
GO

ALTER TABLE [dbo].[Customers] NOCHECK CONSTRAINT [FK_Customers_ScenarioTypes_ScenarioId]
GO
ALTER TABLE [dbo].[SalesInvoices] NOCHECK CONSTRAINT [FK_SalesInvoices_ScenarioTypes_ScenarioId]
GO

DELETE FROM [dbo].[ScenarioTypes]
GO

DBCC CHECKIDENT ('[dbo].[ScenarioTypes]', RESEED, 0)
GO

SET IDENTITY_INSERT [dbo].[ScenarioTypes] ON
GO

INSERT INTO [dbo].[ScenarioTypes] ([ScenarioId], [ScenarioType], [Description]) VALUES
    (1, N'SN001',  N'Goods at Standard Rate to Registered Buyers'),
    (2, N'SN002',  N'Goods at Standard Rate to UnRegistered Buyers'),
    (3, N'SN008',  N'Sales of 3rd Schedule Goods'),
    (4, N'SN0026', N'Sale to End Consumer by Retailers'),
    (5, N'SN0027', N'Sale to End Consumer by Retailers'),
    (6, N'SN0028', N'Sale to End Consumer by Retailers')
GO

SET IDENTITY_INSERT [dbo].[ScenarioTypes] OFF
GO

-- Re-point any existing customers to SN001 (1) after reseed
UPDATE [dbo].[Customers]
SET [ScenarioId] = 1
WHERE [ScenarioId] NOT IN (SELECT [ScenarioId] FROM [dbo].[ScenarioTypes])
GO

ALTER TABLE [dbo].[Customers] WITH CHECK CHECK CONSTRAINT [FK_Customers_ScenarioTypes_ScenarioId]
GO
ALTER TABLE [dbo].[SalesInvoices] WITH CHECK CHECK CONSTRAINT [FK_SalesInvoices_ScenarioTypes_ScenarioId]
GO

-- ---------- ASP.NET Roles ----------
DELETE FROM [dbo].[RolePermissions]
GO
DELETE FROM [dbo].[AspNetUserRoles]
GO
DELETE FROM [dbo].[AspNetRoleClaims]
GO
DELETE FROM [dbo].[AspNetRoles]
GO

INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp]) VALUES
    (N'b1111111-1111-1111-1111-111111111101', N'SuperAdmin',   N'SUPERADMIN',   CONVERT(nvarchar(36), NEWID())),
    (N'b1111111-1111-1111-1111-111111111102', N'Admin',        N'ADMIN',        CONVERT(nvarchar(36), NEWID())),
    (N'b1111111-1111-1111-1111-111111111103', N'Accountant',   N'ACCOUNTANT',   CONVERT(nvarchar(36), NEWID())),
    (N'b1111111-1111-1111-1111-111111111104', N'SalesUser',    N'SALESUSER',    CONVERT(nvarchar(36), NEWID())),
    (N'b1111111-1111-1111-1111-111111111105', N'PurchaseUser', N'PURCHASEUSER', CONVERT(nvarchar(36), NEWID())),
    (N'b1111111-1111-1111-1111-111111111106', N'ReportsUser',  N'REPORTSUSER',  CONVERT(nvarchar(36), NEWID()))
GO

PRINT 'PakistanAccountingERP schema v6 applied successfully.'
GO
