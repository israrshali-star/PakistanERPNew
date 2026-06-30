/*
  MIA Company (Id 3): align cash/bank COA openings with QuickBooks TB (31-May-2026).

  Source: scripts/tb_check.csv (QB Trial Balance export)

  Fixes:
    10015 Cash on Hand (QB 10800): 45,457.77 -> 1,120,909.77
    10016 Kept Aside (QB 10900): 0 -> 110,000.00

  Safe to re-run.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @CompanyId INT = 3;
DECLARE @Now DATETIME2 = SYSUTCDATETIME();
DECLARE @User NVARCHAR(100) = N'qb-tb-bank-openings';

BEGIN TRANSACTION;

UPDATE coa
SET OpeningBalance = 1120909.77,
    UpdatedAt = @Now,
    UpdatedBy = @User
FROM ChartOfAccounts coa
WHERE coa.CompanyId = @CompanyId
  AND coa.IsDeleted = 0
  AND coa.AccountNumber = N'10015';

UPDATE coa
SET OpeningBalance = 110000.00,
    UpdatedAt = @Now,
    UpdatedBy = @User
FROM ChartOfAccounts coa
WHERE coa.CompanyId = @CompanyId
  AND coa.IsDeleted = 0
  AND coa.AccountNumber = N'10016';

-- Sync linked bank master opening + GL closing balance
DECLARE @CoaId INT;
DECLARE @GlClosing DECIMAL(18, 2);
DECLARE @Opening DECIMAL(18, 2);

DECLARE bank_cursor CURSOR LOCAL FAST_FORWARD FOR
SELECT
    coa.Id,
    coa.OpeningBalance,
    coa.OpeningBalance
        + ISNULL(jt.Dr, 0)
        - ISNULL(jt.Cr, 0)
FROM ChartOfAccounts coa
CROSS APPLY (
    SELECT
        SUM(jel.Debit) AS Dr,
        SUM(jel.Credit) AS Cr
    FROM JournalEntryLines jel
    INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId
    WHERE jel.ChartOfAccountId = coa.Id
      AND je.CompanyId = @CompanyId
      AND je.IsDeleted = 0
      AND je.Status = 2
) jt
WHERE coa.CompanyId = @CompanyId
  AND coa.IsDeleted = 0
  AND coa.SubTypeId = 1
  AND NOT EXISTS (
      SELECT 1
      FROM ChartOfAccounts c2
      WHERE c2.ParentAccountId = coa.Id AND c2.IsDeleted = 0
  );

OPEN bank_cursor;
FETCH NEXT FROM bank_cursor INTO @CoaId, @Opening, @GlClosing;

WHILE @@FETCH_STATUS = 0
BEGIN
    UPDATE b
    SET OpeningBalance = @Opening,
        CurrentBalance = @GlClosing,
        UpdatedAt = @Now,
        UpdatedBy = @User
    FROM Banks b
    WHERE b.CompanyId = @CompanyId
      AND b.IsDeleted = 0
      AND b.ChartOfAccountId = @CoaId;

    FETCH NEXT FROM bank_cursor INTO @CoaId, @Opening, @GlClosing;
END;

CLOSE bank_cursor;
DEALLOCATE bank_cursor;

COMMIT TRANSACTION;

SELECT
    coa.AccountNumber,
    coa.AccountName,
    coa.OpeningBalance,
    ISNULL(jt.Dr, 0) AS GlDebits,
    ISNULL(jt.Cr, 0) AS GlCredits,
    coa.OpeningBalance + ISNULL(jt.Dr, 0) - ISNULL(jt.Cr, 0) AS GlClosing
FROM ChartOfAccounts coa
CROSS APPLY (
    SELECT SUM(jel.Debit) AS Dr, SUM(jel.Credit) AS Cr
    FROM JournalEntryLines jel
    INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId
    WHERE jel.ChartOfAccountId = coa.Id
      AND je.CompanyId = @CompanyId
      AND je.IsDeleted = 0
      AND je.Status = 2
) jt
WHERE coa.CompanyId = @CompanyId
  AND coa.IsDeleted = 0
  AND coa.SubTypeId = 1
  AND NOT EXISTS (
      SELECT 1 FROM ChartOfAccounts c2
      WHERE c2.ParentAccountId = coa.Id AND c2.IsDeleted = 0
  )
ORDER BY coa.AccountNumber;
