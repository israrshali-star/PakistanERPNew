/*
  Remap legacy chart-of-account numbers to standard GL accounts.
  Safe to re-run: only remaps lines still pointing at legacy account numbers.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @Now DATETIME2 = SYSUTCDATETIME();
DECLARE @Remap TABLE (OldNumber NVARCHAR(20), NewNumber NVARCHAR(20), NewName NVARCHAR(200));

INSERT INTO @Remap (OldNumber, NewNumber, NewName) VALUES
    (N'1200', N'11110', N'Accounts Receivable'),
    (N'1300', N'12110', N'Inventory Asset'),
    (N'1400', N'12910', N'Pre Paid Sales Tax'),
    (N'2100', N'20000', N'Account Payable'),
    (N'2200', N'25500', N'Sales Tax Payable'),
    (N'4100', N'47910', N'Sales Revenue');

DECLARE @CompanyId INT;
DECLARE company_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT Id FROM Companies;

OPEN company_cursor;
FETCH NEXT FROM company_cursor INTO @CompanyId;

WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @OldNumber NVARCHAR(20);
    DECLARE @NewNumber NVARCHAR(20);
    DECLARE @NewName NVARCHAR(200);

    DECLARE remap_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT OldNumber, NewNumber, NewName FROM @Remap;

    OPEN remap_cursor;
    FETCH NEXT FROM remap_cursor INTO @OldNumber, @NewNumber, @NewName;

    WHILE @@FETCH_STATUS = 0
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
                    WHEN N'11110' THEN 1 WHEN N'12110' THEN 1 WHEN N'12910' THEN 1
                    WHEN N'20000' THEN 2 WHEN N'25500' THEN 2 WHEN N'26100' THEN 2
                    WHEN N'47910' THEN 4 ELSE 1 END;
                SET @SubTypeId = CASE @NewNumber
                    WHEN N'11110' THEN 2 WHEN N'12110' THEN 3 WHEN N'12910' THEN 6
                    WHEN N'20000' THEN 8 WHEN N'25500' THEN 10 WHEN N'26100' THEN 9
                    WHEN N'47910' THEN 18 ELSE 1 END;
            END

            INSERT INTO ChartOfAccounts (
                CompanyId, AccountNumber, AccountName, TypeId, SubTypeId,
                IsActive, OpeningBalance, CreatedAt, CreatedBy)
            VALUES (
                @CompanyId, @NewNumber, @NewName, @TypeId, @SubTypeId,
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

        FETCH NEXT FROM remap_cursor INTO @OldNumber, @NewNumber, @NewName;
    END

    CLOSE remap_cursor;
    DEALLOCATE remap_cursor;

    IF NOT EXISTS (
        SELECT 1 FROM ChartOfAccounts
        WHERE CompanyId = @CompanyId AND AccountNumber = N'26100')
    BEGIN
        INSERT INTO ChartOfAccounts (
            CompanyId, AccountNumber, AccountName, TypeId, SubTypeId,
            IsActive, OpeningBalance, CreatedAt, CreatedBy)
        VALUES (@CompanyId, N'26100', N'Cartage Payable', 2, 9, 1, 0, @Now, N'system-migration');
    END

    FETCH NEXT FROM company_cursor INTO @CompanyId;
END

CLOSE company_cursor;
DEALLOCATE company_cursor;

/* Fix posted sales invoices that included ITEM-0002 cartage in Sales Revenue */
DECLARE @CartageFix TABLE (JournalEntryId INT, CartageAmount DECIMAL(18,2), RevenueAccountId INT, CartageAccountId INT);

INSERT INTO @CartageFix (JournalEntryId, CartageAmount, RevenueAccountId, CartageAccountId)
SELECT
    si.JournalEntryId,
    SUM(sil.LineTotal),
    rev.Id,
    cart.Id
FROM SalesInvoices si
INNER JOIN SalesInvoiceLines sil ON sil.SalesInvoiceId = si.Id
INNER JOIN Items i ON i.Id = sil.ItemId AND i.ItemCode = N'ITEM-0002'
INNER JOIN JournalEntries je ON je.Id = si.JournalEntryId
INNER JOIN ChartOfAccounts rev ON rev.CompanyId = si.CompanyId AND rev.AccountNumber = N'47910'
INNER JOIN ChartOfAccounts cart ON cart.CompanyId = si.CompanyId AND cart.AccountNumber = N'26100'
WHERE si.JournalEntryId IS NOT NULL
GROUP BY si.JournalEntryId, rev.Id, cart.Id
HAVING SUM(sil.LineTotal) > 0;

UPDATE jel
SET Credit = jel.Credit - cf.CartageAmount
FROM JournalEntryLines jel
INNER JOIN @CartageFix cf ON cf.JournalEntryId = jel.JournalEntryId
    AND jel.ChartOfAccountId = cf.RevenueAccountId
WHERE jel.Credit >= cf.CartageAmount;

INSERT INTO JournalEntryLines (JournalEntryId, ChartOfAccountId, Debit, Credit, Memo)
SELECT cf.JournalEntryId, cf.CartageAccountId, 0, cf.CartageAmount, N'Cartage Payable'
FROM @CartageFix cf
WHERE NOT EXISTS (
    SELECT 1 FROM JournalEntryLines existing
    WHERE existing.JournalEntryId = cf.JournalEntryId
      AND existing.ChartOfAccountId = cf.CartageAccountId
      AND existing.Memo = N'Cartage Payable');

/* Delete legacy duplicate accounts with no remaining references */
DELETE coa
FROM ChartOfAccounts coa
WHERE coa.AccountNumber IN (N'1200', N'1300', N'1400', N'2100', N'2200', N'4100')
  AND NOT EXISTS (SELECT 1 FROM JournalEntryLines jel WHERE jel.ChartOfAccountId = coa.Id)
  AND NOT EXISTS (SELECT 1 FROM Banks b WHERE b.ChartOfAccountId = coa.Id)
  AND NOT EXISTS (SELECT 1 FROM ChartOfAccounts child WHERE child.ParentAccountId = coa.Id);

COMMIT TRANSACTION;

PRINT 'Chart of accounts remap completed.';
