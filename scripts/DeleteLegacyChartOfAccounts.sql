/*
  Delete legacy chart-of-account rows listed for cleanup.
  - Remaps journal lines and bank links where a replacement account exists (1100->10015, 3200->32010, 1300->12110).
  - Reparents child accounts from legacy parents before delete.
  - Skips accounts that still have journal-entry line references after remap.
  Safe to re-run.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

BEGIN TRANSACTION;

DECLARE @Now DATETIME2 = SYSUTCDATETIME();

DECLARE @Legacy TABLE (
    OldNumber NVARCHAR(20) NOT NULL,
    NewNumber NVARCHAR(20) NULL,
    AccountName NVARCHAR(200) NOT NULL);

INSERT INTO @Legacy (OldNumber, NewNumber, AccountName) VALUES
    (N'1100', N'10015', N'Cash In Hand'),
    (N'1300', N'12110', N'Inventory'),
    (N'1500', NULL,     N'Fixed Assets'),
    (N'2300', NULL,     N'Accrued Liabilities'),
    (N'3100', NULL,     N'Owner''s Capital'),
    (N'3200', N'32010', N'Retained Earnings'),
    (N'4200', NULL,     N'Sales Returns'),
    (N'5100', NULL,     N'Purchases'),
    (N'5200', NULL,     N'Freight In'),
    (N'6100', NULL,     N'Administrative Expenses'),
    (N'6200', NULL,     N'Selling & Marketing'),
    (N'6300', NULL,     N'Payroll & Benefits');

DECLARE @Deleted TABLE (
    CompanyId INT,
    CompanyName NVARCHAR(200),
    CoaId INT,
    AccountNumber NVARCHAR(20),
    AccountName NVARCHAR(200));

DECLARE @Blocked TABLE (
    CompanyId INT,
    CompanyName NVARCHAR(200),
    CoaId INT,
    AccountNumber NVARCHAR(20),
    AccountName NVARCHAR(200),
    JeLineCount INT);

DECLARE @CompanyId INT;
DECLARE company_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT Id FROM Companies WHERE IsDeleted = 0;

OPEN company_cursor;
FETCH NEXT FROM company_cursor INTO @CompanyId;

WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @OldNumber NVARCHAR(20);
    DECLARE @NewNumber NVARCHAR(20);
    DECLARE @AccountName NVARCHAR(200);

    DECLARE legacy_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT OldNumber, NewNumber, AccountName FROM @Legacy;

    OPEN legacy_cursor;
    FETCH NEXT FROM legacy_cursor INTO @OldNumber, @NewNumber, @AccountName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @NewNumber IS NOT NULL
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM ChartOfAccounts
                WHERE CompanyId = @CompanyId AND AccountNumber = @NewNumber)
            BEGIN
                DECLARE @TypeId INT = NULL;
                DECLARE @SubTypeId INT = NULL;

                SELECT TOP 1 @TypeId = TypeId, @SubTypeId = SubTypeId
                FROM ChartOfAccounts
                WHERE CompanyId = @CompanyId AND AccountNumber = @OldNumber
                ORDER BY Id;

                IF @TypeId IS NULL
                BEGIN
                    SET @TypeId = CASE @NewNumber
                        WHEN N'10015' THEN 1 WHEN N'12110' THEN 1 WHEN N'32010' THEN 3 ELSE 1 END;
                    SET @SubTypeId = CASE @NewNumber
                        WHEN N'10015' THEN 1 WHEN N'12110' THEN 3 WHEN N'32010' THEN 15 ELSE 1 END;
                END

                INSERT INTO ChartOfAccounts (
                    CompanyId, AccountNumber, AccountName, TypeId, SubTypeId,
                    IsActive, OpeningBalance, CreatedAt, CreatedBy)
                VALUES (
                    @CompanyId, @NewNumber, @AccountName, @TypeId, @SubTypeId,
                    1, 0, @Now, N'system-migration');
            END

            DECLARE @TargetId INT = (
                SELECT TOP 1 Id FROM ChartOfAccounts
                WHERE CompanyId = @CompanyId AND AccountNumber = @NewNumber
                ORDER BY Id);

            UPDATE jel
            SET ChartOfAccountId = @TargetId
            FROM JournalEntryLines jel
            INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId
            INNER JOIN ChartOfAccounts oldCoa ON oldCoa.Id = jel.ChartOfAccountId
            WHERE je.CompanyId = @CompanyId
              AND oldCoa.CompanyId = @CompanyId
              AND oldCoa.AccountNumber = @OldNumber
              AND jel.ChartOfAccountId <> @TargetId;

            UPDATE b
            SET ChartOfAccountId = @TargetId
            FROM Banks b
            INNER JOIN ChartOfAccounts oldCoa ON oldCoa.Id = b.ChartOfAccountId
            WHERE b.CompanyId = @CompanyId
              AND oldCoa.CompanyId = @CompanyId
              AND oldCoa.AccountNumber = @OldNumber
              AND (b.ChartOfAccountId IS NULL OR b.ChartOfAccountId <> @TargetId);

            UPDATE child
            SET ParentAccountId = @TargetId
            FROM ChartOfAccounts child
            INNER JOIN ChartOfAccounts oldCoa ON oldCoa.Id = child.ParentAccountId
            WHERE oldCoa.CompanyId = @CompanyId
              AND oldCoa.AccountNumber = @OldNumber
              AND child.ParentAccountId <> @TargetId;
        END

        FETCH NEXT FROM legacy_cursor INTO @OldNumber, @NewNumber, @AccountName;
    END

    CLOSE legacy_cursor;
    DEALLOCATE legacy_cursor;

    FETCH NEXT FROM company_cursor INTO @CompanyId;
END

CLOSE company_cursor;
DEALLOCATE company_cursor;

DELETE coa
OUTPUT
    deleted.CompanyId,
    c.CompanyName,
    deleted.Id,
    deleted.AccountNumber,
    deleted.AccountName
INTO @Deleted (CompanyId, CompanyName, CoaId, AccountNumber, AccountName)
FROM ChartOfAccounts coa
INNER JOIN Companies c ON c.Id = coa.CompanyId
INNER JOIN @Legacy l ON l.OldNumber = coa.AccountNumber
WHERE NOT EXISTS (SELECT 1 FROM JournalEntryLines jel WHERE jel.ChartOfAccountId = coa.Id)
  AND NOT EXISTS (SELECT 1 FROM Banks b WHERE b.ChartOfAccountId = coa.Id)
  AND NOT EXISTS (SELECT 1 FROM ChartOfAccounts child WHERE child.ParentAccountId = coa.Id);

INSERT INTO @Blocked (CompanyId, CompanyName, CoaId, AccountNumber, AccountName, JeLineCount)
SELECT
    coa.CompanyId,
    c.CompanyName,
    coa.Id,
    coa.AccountNumber,
    coa.AccountName,
    (SELECT COUNT(*) FROM JournalEntryLines jel WHERE jel.ChartOfAccountId = coa.Id)
FROM ChartOfAccounts coa
INNER JOIN Companies c ON c.Id = coa.CompanyId
INNER JOIN @Legacy l ON l.OldNumber = coa.AccountNumber;

COMMIT TRANSACTION;

PRINT '=== Deleted legacy chart-of-account rows ===';
SELECT CompanyId, CompanyName, CoaId, AccountNumber, AccountName
FROM @Deleted
ORDER BY AccountNumber, CompanyId, CoaId;

PRINT '=== Blocked (still present) ===';
SELECT CompanyId, CompanyName, CoaId, AccountNumber, AccountName, JeLineCount
FROM @Blocked
ORDER BY AccountNumber, CompanyId, CoaId;

PRINT 'Delete legacy chart of accounts completed.';
