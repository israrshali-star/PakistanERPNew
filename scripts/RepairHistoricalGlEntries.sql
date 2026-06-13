/*
  Repair historical GL entries for all companies (safe to re-run):
    1. Remap legacy COA numbers on journal lines (1200->11110, etc.)
    2. Consolidate parent AR account 11000 -> child AR 11110
    3. Fix ITEM-0002 cartage posted to revenue instead of Cartage Payable
    4. Backfill missing COGS / Inventory Asset lines on posted sales invoices
    5. Soft-delete duplicate/orphan journal entries (invoices, receipts, bank tx, bills)
    6. Resync customer opening balance journals from Customers.OpeningBalance
    7. Purge journal lines belonging to soft-deleted journals

  Run after deploying GlRepairService, or use POST /api/gl-repair/historical from the app.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

BEGIN TRANSACTION;

DECLARE @Now DATETIME2 = SYSUTCDATETIME();
DECLARE @User NVARCHAR(256) = N'gl-repair-script';

/* --- 1. Legacy COA remap on journal lines --- */
DECLARE @Remap TABLE (OldNumber NVARCHAR(20), NewNumber NVARCHAR(20));
INSERT INTO @Remap VALUES
    (N'1200', N'11110'),
    (N'1300', N'12110'),
    (N'1400', N'12910'),
    (N'2100', N'20000'),
    (N'2200', N'25500'),
    (N'4100', N'47910'),
    (N'1100', N'10015'),
    (N'3200', N'32010');

DECLARE @LegacyRemapped INT = 0;

UPDATE jel
SET ChartOfAccountId = newCoa.Id
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId AND je.IsDeleted = 0
INNER JOIN ChartOfAccounts oldCoa ON oldCoa.Id = jel.ChartOfAccountId
INNER JOIN @Remap r ON r.OldNumber = oldCoa.AccountNumber
INNER JOIN ChartOfAccounts newCoa
    ON newCoa.CompanyId = je.CompanyId
   AND newCoa.AccountNumber = r.NewNumber
WHERE jel.ChartOfAccountId <> newCoa.Id;

SET @LegacyRemapped = @@ROWCOUNT;

/* --- 2. Consolidate parent AR 11000 -> child AR 11110 --- */
DECLARE @ParentArConsolidated INT = 0;

UPDATE jel
SET ChartOfAccountId = childCoa.Id
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId AND je.IsDeleted = 0
INNER JOIN ChartOfAccounts parentCoa ON parentCoa.Id = jel.ChartOfAccountId AND parentCoa.AccountNumber = N'11000'
INNER JOIN ChartOfAccounts childCoa
    ON childCoa.CompanyId = je.CompanyId
   AND childCoa.AccountNumber = N'11110'
WHERE jel.ChartOfAccountId <> childCoa.Id;

SET @ParentArConsolidated = @@ROWCOUNT;

/* --- 3. Cartage revenue fix --- */
IF OBJECT_ID('tempdb..#CartageFix') IS NOT NULL DROP TABLE #CartageFix;
CREATE TABLE #CartageFix (JournalEntryId INT PRIMARY KEY, CartageAmount DECIMAL(18,2), RevenueAccountId INT, CartageAccountId INT);

INSERT INTO #CartageFix (JournalEntryId, CartageAmount, RevenueAccountId, CartageAccountId)
SELECT
    si.JournalEntryId,
    SUM(sil.LineTotal),
    rev.Id,
    cart.Id
FROM SalesInvoices si
INNER JOIN SalesInvoiceLines sil ON sil.SalesInvoiceId = si.Id
INNER JOIN Items i ON i.Id = sil.ItemId AND i.ItemCode = N'ITEM-0002'
INNER JOIN JournalEntries je ON je.Id = si.JournalEntryId AND je.IsDeleted = 0
INNER JOIN ChartOfAccounts rev ON rev.CompanyId = si.CompanyId AND rev.AccountNumber = N'47910'
INNER JOIN ChartOfAccounts cart ON cart.CompanyId = si.CompanyId AND cart.AccountNumber = N'26100'
WHERE si.JournalEntryId IS NOT NULL
  AND si.Status = 2
GROUP BY si.JournalEntryId, rev.Id, cart.Id
HAVING SUM(sil.LineTotal) > 0;

DECLARE @CartageAdjusted INT = 0;
UPDATE jel
SET Credit = jel.Credit - cf.CartageAmount
FROM JournalEntryLines jel
INNER JOIN #CartageFix cf ON cf.JournalEntryId = jel.JournalEntryId AND jel.ChartOfAccountId = cf.RevenueAccountId
WHERE jel.Credit >= cf.CartageAmount
  AND NOT EXISTS (
      SELECT 1 FROM JournalEntryLines x
      WHERE x.JournalEntryId = cf.JournalEntryId AND x.ChartOfAccountId = cf.CartageAccountId AND x.Memo = N'Cartage Payable');

SET @CartageAdjusted = @@ROWCOUNT;

DECLARE @CartageAdded INT = 0;
INSERT INTO JournalEntryLines (JournalEntryId, ChartOfAccountId, Debit, Credit, Memo)
SELECT cf.JournalEntryId, cf.CartageAccountId, 0, cf.CartageAmount, N'Cartage Payable'
FROM #CartageFix cf
WHERE NOT EXISTS (
    SELECT 1 FROM JournalEntryLines existing
    WHERE existing.JournalEntryId = cf.JournalEntryId
      AND existing.ChartOfAccountId = cf.CartageAccountId
      AND existing.Memo = N'Cartage Payable');

SET @CartageAdded = @@ROWCOUNT;

/* --- 4. Backfill COGS / Inventory on posted sales invoices --- */
IF OBJECT_ID('tempdb..#CogsBackfill') IS NOT NULL DROP TABLE #CogsBackfill;
CREATE TABLE #CogsBackfill
(
    JournalEntryId INT NOT NULL,
    CompanyId INT NOT NULL,
    InvoiceType INT NOT NULL,
    CogsAmount DECIMAL(18,2) NOT NULL,
    CogsAccountId INT NOT NULL,
    InventoryAccountId INT NOT NULL
);

INSERT INTO #CogsBackfill (JournalEntryId, CompanyId, InvoiceType, CogsAmount, CogsAccountId, InventoryAccountId)
SELECT
    si.JournalEntryId,
    si.CompanyId,
    si.InvoiceType,
    ROUND(SUM(ROUND(sil.Quantity, 2) * ROUND(i.PurchaseRate, 2)), 2) AS CogsAmount,
    cogs.Id,
    inv.Id
FROM SalesInvoices si
INNER JOIN SalesInvoiceLines sil ON sil.SalesInvoiceId = si.Id
INNER JOIN Items i ON i.Id = sil.ItemId
INNER JOIN JournalEntries je ON je.Id = si.JournalEntryId AND je.IsDeleted = 0
INNER JOIN ChartOfAccounts cogs ON cogs.CompanyId = si.CompanyId AND cogs.AccountNumber = N'50000'
INNER JOIN ChartOfAccounts inv ON inv.CompanyId = si.CompanyId AND inv.AccountNumber = N'12110'
WHERE si.Status = 2
  AND si.InvoiceType IN (1, 3)
  AND si.JournalEntryId IS NOT NULL
  AND i.ItemType = 1
  AND i.ItemCode <> N'ITEM-0002'
  AND ROUND(sil.Quantity, 2) > 0
  AND NOT EXISTS (
      SELECT 1 FROM JournalEntryLines existing
      WHERE existing.JournalEntryId = si.JournalEntryId
        AND existing.ChartOfAccountId = cogs.Id)
GROUP BY si.JournalEntryId, si.CompanyId, si.InvoiceType, cogs.Id, inv.Id
HAVING ROUND(SUM(ROUND(sil.Quantity, 2) * ROUND(i.PurchaseRate, 2)), 2) > 0;

DECLARE @CogsLinesAdded INT = 0;

INSERT INTO JournalEntryLines (JournalEntryId, ChartOfAccountId, Debit, Credit, Memo)
SELECT JournalEntryId, CogsAccountId, CogsAmount, 0, N'Cost of Goods Sold'
FROM #CogsBackfill WHERE InvoiceType = 1;
SET @CogsLinesAdded += @@ROWCOUNT;

INSERT INTO JournalEntryLines (JournalEntryId, ChartOfAccountId, Debit, Credit, Memo)
SELECT JournalEntryId, InventoryAccountId, 0, CogsAmount, N'Inventory Asset'
FROM #CogsBackfill WHERE InvoiceType = 1;
SET @CogsLinesAdded += @@ROWCOUNT;

INSERT INTO JournalEntryLines (JournalEntryId, ChartOfAccountId, Debit, Credit, Memo)
SELECT JournalEntryId, InventoryAccountId, CogsAmount, 0, N'Inventory Asset'
FROM #CogsBackfill WHERE InvoiceType = 3;
SET @CogsLinesAdded += @@ROWCOUNT;

INSERT INTO JournalEntryLines (JournalEntryId, ChartOfAccountId, Debit, Credit, Memo)
SELECT JournalEntryId, CogsAccountId, 0, CogsAmount, N'Cost of Goods Sold'
FROM #CogsBackfill WHERE InvoiceType = 3;
SET @CogsLinesAdded += @@ROWCOUNT;

/* --- 5. Soft-delete duplicate sales-invoice journals (keep invoice link) --- */
DECLARE @DupDeleted INT = 0;
UPDATE je
SET IsDeleted = 1, DeletedAt = @Now, DeletedBy = @User
FROM JournalEntries je
WHERE je.IsDeleted = 0
  AND je.ReferenceType = N'SalesInvoice'
  AND je.ReferenceId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM SalesInvoices si
      WHERE si.Id = je.ReferenceId AND si.JournalEntryId = je.Id);

SET @DupDeleted = @@ROWCOUNT;

/* --- 6. Soft-delete duplicate customer-receipt journals (keep AR credit matching amount) --- */
DECLARE @DupReceiptDeleted INT = 0;

UPDATE je
SET IsDeleted = 1, DeletedAt = @Now, DeletedBy = @User
FROM JournalEntries je
INNER JOIN (
    SELECT je2.ReferenceId, je2.Id AS JournalId,
           ROW_NUMBER() OVER (
               PARTITION BY je2.ReferenceId
               ORDER BY
                   CASE WHEN EXISTS (
                       SELECT 1 FROM JournalEntryLines jel
                       INNER JOIN ChartOfAccounts ar ON ar.Id = jel.ChartOfAccountId AND ar.AccountNumber = N'11110'
                       INNER JOIN CustomerReceipts r ON r.Id = je2.ReferenceId AND r.IsDeleted = 0
                       WHERE jel.JournalEntryId = je2.Id AND jel.Credit = ROUND(r.Amount, 2)
                   ) THEN 0 ELSE 1 END,
                   je2.Id DESC
           ) AS rn
    FROM JournalEntries je2
    WHERE je2.IsDeleted = 0
      AND je2.ReferenceType = N'CustomerReceipt'
      AND je2.ReferenceId IS NOT NULL
      AND je2.Status = 2
) ranked ON ranked.JournalId = je.Id
WHERE ranked.rn > 1;

SET @DupReceiptDeleted = @@ROWCOUNT;

/* --- 7. Soft-delete duplicate bank-transaction journals (keep linked journal) --- */
DECLARE @DupBankTxDeleted INT = 0;

UPDATE je
SET IsDeleted = 1, DeletedAt = @Now, DeletedBy = @User
FROM JournalEntries je
INNER JOIN (
    SELECT je2.ReferenceId, je2.Id AS JournalId,
           ROW_NUMBER() OVER (
               PARTITION BY je2.ReferenceId
               ORDER BY
                   CASE WHEN bt.JournalEntryId = je2.Id THEN 0 ELSE 1 END,
                   je2.Id DESC
           ) AS rn
    FROM JournalEntries je2
    LEFT JOIN BankTransactions bt ON bt.Id = je2.ReferenceId AND bt.IsDeleted = 0
    WHERE je2.IsDeleted = 0
      AND je2.ReferenceType = N'BankTransaction'
      AND je2.ReferenceId IS NOT NULL
      AND je2.Status = 2
) ranked ON ranked.JournalId = je.Id
WHERE ranked.rn > 1;

SET @DupBankTxDeleted = @@ROWCOUNT;

/* --- 8. Soft-delete orphan receipt/bill/bank journals --- */
DECLARE @OrphanDeleted INT = 0;

UPDATE je
SET IsDeleted = 1, DeletedAt = @Now, DeletedBy = @User
FROM JournalEntries je
WHERE je.IsDeleted = 0
  AND je.ReferenceType = N'CustomerReceipt'
  AND je.ReferenceId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM CustomerReceipts r
      WHERE r.Id = je.ReferenceId AND r.IsDeleted = 0);

SET @OrphanDeleted += @@ROWCOUNT;

UPDATE je
SET IsDeleted = 1, DeletedAt = @Now, DeletedBy = @User
FROM JournalEntries je
WHERE je.IsDeleted = 0
  AND je.ReferenceType = N'VendorBill'
  AND je.ReferenceId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM VendorBills b
      WHERE b.Id = je.ReferenceId AND b.JournalEntryId = je.Id);

SET @OrphanDeleted += @@ROWCOUNT;

UPDATE je
SET IsDeleted = 1, DeletedAt = @Now, DeletedBy = @User
FROM JournalEntries je
WHERE je.IsDeleted = 0
  AND je.ReferenceType = N'BankTransaction'
  AND je.ReferenceId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM BankTransactions bt
      WHERE bt.Id = je.ReferenceId AND bt.IsDeleted = 0 AND bt.JournalEntryId = je.Id);

SET @OrphanDeleted += @@ROWCOUNT;

/* --- 9. Resync customer opening balance journals --- */
DECLARE @CustomerObResynced INT = 0;

UPDATE je
SET IsDeleted = 1, DeletedAt = @Now, DeletedBy = @User
FROM JournalEntries je
WHERE je.IsDeleted = 0
  AND je.ReferenceType = N'Customer'
  AND je.Status = 2;

IF OBJECT_ID('tempdb..#CustomerOb') IS NOT NULL DROP TABLE #CustomerOb;
CREATE TABLE #CustomerOb
(
    CompanyId INT NOT NULL,
    CustomerId INT NOT NULL,
    BuyerName NVARCHAR(256) NOT NULL,
    OpeningBalance DECIMAL(18,2) NOT NULL,
    ArAccountId INT NOT NULL,
    EquityAccountId INT NOT NULL,
    EntryNumber NVARCHAR(32) NOT NULL,
    EntryDate DATE NOT NULL
);

INSERT INTO #CustomerOb (CompanyId, CustomerId, BuyerName, OpeningBalance, ArAccountId, EquityAccountId, EntryNumber, EntryDate)
SELECT
    c.CompanyId,
    c.Id,
    LTRIM(RTRIM(c.BuyerName)),
    ROUND(c.OpeningBalance, 2),
    ar.Id,
    eq.Id,
    N'JE-' + RIGHT(N'0000' + CAST(
        ISNULL((
            SELECT MAX(TRY_CAST(SUBSTRING(j.EntryNumber, 4, 20) AS INT))
            FROM JournalEntries j
            WHERE j.CompanyId = c.CompanyId AND j.EntryNumber LIKE N'JE-%'
        ), 0) + ROW_NUMBER() OVER (PARTITION BY c.CompanyId ORDER BY c.Id) AS NVARCHAR(20)), 4),
    CAST(SYSUTCDATETIME() AS DATE)
FROM Customers c
INNER JOIN ChartOfAccounts ar ON ar.CompanyId = c.CompanyId AND ar.AccountNumber = N'11110' AND ar.IsActive = 1
INNER JOIN ChartOfAccounts eq ON eq.CompanyId = c.CompanyId AND eq.AccountNumber = N'32010' AND eq.IsActive = 1
WHERE c.IsDeleted = 0
  AND ROUND(c.OpeningBalance, 2) <> 0;

INSERT INTO JournalEntries (CompanyId, EntryNumber, EntryDate, Description, ReferenceType, ReferenceId, Status, CreatedAt, CreatedBy, IsDeleted)
SELECT
    cob.CompanyId,
    cob.EntryNumber,
    cob.EntryDate,
    N'Customer opening balance — ' + cob.BuyerName,
    N'Customer',
    cob.CustomerId,
    2,
    @Now,
    @User,
    0
FROM #CustomerOb cob;

INSERT INTO JournalEntryLines (JournalEntryId, ChartOfAccountId, Debit, Credit, Memo)
SELECT je.Id, cob.ArAccountId, ABS(cob.OpeningBalance), 0, N'Accounts Receivable'
FROM #CustomerOb cob
INNER JOIN JournalEntries je
    ON je.CompanyId = cob.CompanyId
   AND je.ReferenceType = N'Customer'
   AND je.ReferenceId = cob.CustomerId
   AND je.IsDeleted = 0
   AND je.CreatedAt = @Now
   AND je.CreatedBy = @User
WHERE cob.OpeningBalance > 0;

INSERT INTO JournalEntryLines (JournalEntryId, ChartOfAccountId, Debit, Credit, Memo)
SELECT je.Id, cob.EquityAccountId, 0, ABS(cob.OpeningBalance), N'Opening balance offset'
FROM #CustomerOb cob
INNER JOIN JournalEntries je
    ON je.CompanyId = cob.CompanyId
   AND je.ReferenceType = N'Customer'
   AND je.ReferenceId = cob.CustomerId
   AND je.IsDeleted = 0
   AND je.CreatedAt = @Now
   AND je.CreatedBy = @User
WHERE cob.OpeningBalance > 0;

INSERT INTO JournalEntryLines (JournalEntryId, ChartOfAccountId, Debit, Credit, Memo)
SELECT je.Id, cob.EquityAccountId, ABS(cob.OpeningBalance), 0, N'Opening balance offset'
FROM #CustomerOb cob
INNER JOIN JournalEntries je
    ON je.CompanyId = cob.CompanyId
   AND je.ReferenceType = N'Customer'
   AND je.ReferenceId = cob.CustomerId
   AND je.IsDeleted = 0
   AND je.CreatedAt = @Now
   AND je.CreatedBy = @User
WHERE cob.OpeningBalance < 0;

INSERT INTO JournalEntryLines (JournalEntryId, ChartOfAccountId, Debit, Credit, Memo)
SELECT je.Id, cob.ArAccountId, 0, ABS(cob.OpeningBalance), N'Accounts Receivable'
FROM #CustomerOb cob
INNER JOIN JournalEntries je
    ON je.CompanyId = cob.CompanyId
   AND je.ReferenceType = N'Customer'
   AND je.ReferenceId = cob.CustomerId
   AND je.IsDeleted = 0
   AND je.CreatedAt = @Now
   AND je.CreatedBy = @User
WHERE cob.OpeningBalance < 0;

SET @CustomerObResynced = (SELECT COUNT(*) FROM #CustomerOb);

/* --- 10. Purge journal lines on soft-deleted journals --- */
DECLARE @DeletedLinesPurged INT = 0;

DELETE jel
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId AND je.IsDeleted = 1;

SET @DeletedLinesPurged = @@ROWCOUNT;

/* --- 11. Clear bogus imported COGS opening balances (COGS comes from invoice postings) --- */
UPDATE coa
SET OpeningBalance = 0, UpdatedAt = @Now, UpdatedBy = @User
FROM ChartOfAccounts coa
WHERE coa.AccountNumber = N'50000'
  AND coa.OpeningBalance <> 0
  AND ABS(coa.OpeningBalance) > 1000000
  AND ABS(coa.OpeningBalance) > ABS(ISNULL((
      SELECT SUM(jel.Debit - jel.Credit)
      FROM JournalEntryLines jel
      INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId AND je.IsDeleted = 0 AND je.Status = 2
      WHERE jel.ChartOfAccountId = coa.Id), 0)) * 10;

COMMIT TRANSACTION;

PRINT N'Legacy COA lines remapped: ' + CAST(@LegacyRemapped AS NVARCHAR(20));
PRINT N'Parent AR lines consolidated: ' + CAST(@ParentArConsolidated AS NVARCHAR(20));
PRINT N'Cartage revenue credits adjusted: ' + CAST(@CartageAdjusted AS NVARCHAR(20));
PRINT N'Cartage payable lines added: ' + CAST(@CartageAdded AS NVARCHAR(20));
PRINT N'COGS/inventory lines added: ' + CAST(@CogsLinesAdded AS NVARCHAR(20));
PRINT N'Duplicate invoice journals soft-deleted: ' + CAST(@DupDeleted AS NVARCHAR(20));
PRINT N'Duplicate receipt journals soft-deleted: ' + CAST(@DupReceiptDeleted AS NVARCHAR(20));
PRINT N'Duplicate bank-tx journals soft-deleted: ' + CAST(@DupBankTxDeleted AS NVARCHAR(20));
PRINT N'Orphan journals soft-deleted: ' + CAST(@OrphanDeleted AS NVARCHAR(20));
PRINT N'Customer OB journals resynced: ' + CAST(@CustomerObResynced AS NVARCHAR(20));
PRINT N'Deleted journal lines purged: ' + CAST(@DeletedLinesPurged AS NVARCHAR(20));

SELECT
    coa.AccountNumber,
    coa.AccountName,
    coa.OpeningBalance
        + ISNULL(SUM(jel.Debit - jel.Credit), 0) AS RunningBalance
FROM ChartOfAccounts coa
LEFT JOIN JournalEntryLines jel ON jel.ChartOfAccountId = coa.Id
LEFT JOIN JournalEntries je ON je.Id = jel.JournalEntryId
    AND je.Status = 2
    AND je.IsDeleted = 0
WHERE coa.AccountNumber IN (N'11110', N'12110', N'50000')
  AND coa.CompanyId = 3
GROUP BY coa.CompanyId, coa.AccountNumber, coa.AccountName, coa.OpeningBalance
ORDER BY coa.CompanyId, coa.AccountNumber;
