/*
  Follow-up fixes after DeleteLegacyChartOfAccounts.sql:
  1. Re-add 4200 Sales Returns to all companies (Type 4 Revenue, SubType 19 Sales Returns).
  2. Remap legacy 5100 Purchases JE lines to 12110 Inventory Asset, then delete 5100.
  Safe to re-run.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

BEGIN TRANSACTION;

DECLARE @Now DATETIME2 = SYSUTCDATETIME();

/* --- Re-add 4200 Sales Returns where missing --- */
INSERT INTO ChartOfAccounts (
    CompanyId, AccountNumber, AccountName, TypeId, SubTypeId,
    IsActive, OpeningBalance, CreatedAt, CreatedBy)
SELECT
    c.Id,
    N'4200',
    N'Sales Returns',
    4,
    19,
    1,
    0,
    @Now,
    N'system-migration'
FROM Companies c
WHERE c.IsDeleted = 0
  AND NOT EXISTS (
      SELECT 1 FROM ChartOfAccounts coa
      WHERE coa.CompanyId = c.Id AND coa.AccountNumber = N'4200');

/* --- Remap 5100 Purchases JE lines to 12110 Inventory Asset (current vendor bill posting) --- */
UPDATE jel
SET ChartOfAccountId = inv.Id
FROM JournalEntryLines jel
INNER JOIN ChartOfAccounts oldCoa ON oldCoa.Id = jel.ChartOfAccountId
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId
INNER JOIN ChartOfAccounts inv
    ON inv.CompanyId = je.CompanyId
   AND inv.AccountNumber = N'12110'
WHERE oldCoa.AccountNumber = N'5100'
  AND jel.ChartOfAccountId <> inv.Id;

UPDATE jel
SET Memo = N'Inventory Asset'
FROM JournalEntryLines jel
INNER JOIN ChartOfAccounts coa ON coa.Id = jel.ChartOfAccountId
WHERE coa.AccountNumber = N'12110'
  AND jel.Memo = N'Purchases';

/* --- Delete 5100 where no remaining references --- */
DELETE coa
FROM ChartOfAccounts coa
WHERE coa.AccountNumber = N'5100'
  AND NOT EXISTS (SELECT 1 FROM JournalEntryLines jel WHERE jel.ChartOfAccountId = coa.Id)
  AND NOT EXISTS (SELECT 1 FROM Banks b WHERE b.ChartOfAccountId = coa.Id)
  AND NOT EXISTS (SELECT 1 FROM ChartOfAccounts child WHERE child.ParentAccountId = coa.Id);

COMMIT TRANSACTION;

PRINT '=== 4200 Sales Returns status ===';
SELECT c.Id AS CompanyId, c.CompanyName, coa.Id AS CoaId, coa.AccountNumber, coa.AccountName, coa.TypeId, coa.SubTypeId
FROM Companies c
INNER JOIN ChartOfAccounts coa ON coa.CompanyId = c.Id AND coa.AccountNumber = N'4200'
WHERE c.IsDeleted = 0
ORDER BY c.Id;

PRINT '=== Remaining 5100 (should be empty) ===';
SELECT c.Id AS CompanyId, c.CompanyName, coa.Id AS CoaId, coa.AccountNumber,
  (SELECT COUNT(*) FROM JournalEntryLines j WHERE j.ChartOfAccountId = coa.Id) AS JeLines
FROM ChartOfAccounts coa
INNER JOIN Companies c ON c.Id = coa.CompanyId
WHERE coa.AccountNumber = N'5100';

PRINT 'Fix legacy COA 4200/5100 completed.';
