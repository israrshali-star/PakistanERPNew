/*
  Delete per-company bank chart-of-account rows (and linked Banks).
  Companies 2,4,5,6,7 only — Company 3 is not modified.
  - Reparents child COA rows to 10000 (Bank Accounts) if parent is deleted.
  - Skips accounts with journal-entry line references.
  Safe to re-run.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

BEGIN TRANSACTION;

DECLARE @Targets TABLE (
    CompanyId INT NOT NULL,
    AccountNumber NVARCHAR(20) NOT NULL,
    PRIMARY KEY (CompanyId, AccountNumber));

INSERT INTO @Targets (CompanyId, AccountNumber) VALUES
    (2, N'10001'), (2, N'10002'), (2, N'10003'), (2, N'10006'), (2, N'10007'),
    (2, N'10008'), (2, N'10009'), (2, N'10010'), (2, N'10011'), (2, N'10012'),
    (2, N'10013'), (2, N'10014'),
    (4, N'10001'), (4, N'10002'), (4, N'10003'), (4, N'10004'), (4, N'10005'),
    (4, N'10007'), (4, N'10008'), (4, N'10009'), (4, N'10010'), (4, N'10011'),
    (4, N'10012'), (4, N'10014'),
    (5, N'10001'), (5, N'10002'), (5, N'10003'), (5, N'10004'), (5, N'10005'),
    (5, N'10006'), (5, N'10008'), (5, N'10009'), (5, N'10010'), (5, N'10011'),
    (5, N'10012'), (5, N'10013'), (5, N'10014'),
    (6, N'10001'), (6, N'10002'), (6, N'10003'), (6, N'10004'), (6, N'10005'),
    (6, N'10006'), (6, N'10007'), (6, N'10008'), (6, N'10009'), (6, N'10011'),
    (6, N'10013'), (6, N'10014'),
    (7, N'10002'), (7, N'10003'), (7, N'10004'), (7, N'10005'), (7, N'10006'),
    (7, N'10007'), (7, N'10008'), (7, N'10010'), (7, N'10011'), (7, N'10012'),
    (7, N'10013'), (7, N'10014');

DECLARE @DeletedBanks TABLE (
    CompanyId INT,
    BankId INT,
    AccountNumber NVARCHAR(20),
    BankName NVARCHAR(200));

DECLARE @DeletedCoa TABLE (
    CompanyId INT,
    CoaId INT,
    AccountNumber NVARCHAR(20),
    AccountName NVARCHAR(200));

DECLARE @Blocked TABLE (
    CompanyId INT,
    CoaId INT,
    AccountNumber NVARCHAR(20),
    AccountName NVARCHAR(200),
    JeLineCount INT,
    Reason NVARCHAR(100));

DECLARE @Reparented TABLE (
    CompanyId INT,
    ChildCoaId INT,
    ChildAccountNumber NVARCHAR(20),
    OldParentCoaId INT,
    NewParentCoaId INT);

-- Accounts to process (must exist on COA for that company)
DECLARE @ToDelete TABLE (
    CompanyId INT NOT NULL,
    CoaId INT NOT NULL,
    AccountNumber NVARCHAR(20) NOT NULL,
    PRIMARY KEY (CoaId));

INSERT INTO @ToDelete (CompanyId, CoaId, AccountNumber)
SELECT t.CompanyId, coa.Id, coa.AccountNumber
FROM @Targets t
INNER JOIN ChartOfAccounts coa
    ON coa.CompanyId = t.CompanyId
   AND coa.AccountNumber = t.AccountNumber;

-- Block JE-referenced accounts
INSERT INTO @Blocked (CompanyId, CoaId, AccountNumber, AccountName, JeLineCount, Reason)
SELECT
    d.CompanyId,
    d.CoaId,
    d.AccountNumber,
    coa.AccountName,
    jeCnt.Cnt,
    N'JournalEntryLines'
FROM @ToDelete d
INNER JOIN ChartOfAccounts coa ON coa.Id = d.CoaId
CROSS APPLY (
    SELECT COUNT(*) AS Cnt FROM JournalEntryLines jel WHERE jel.ChartOfAccountId = d.CoaId
) jeCnt
WHERE jeCnt.Cnt > 0;

DELETE d
FROM @ToDelete d
INNER JOIN @Blocked b ON b.CoaId = d.CoaId;

-- Reparent children of doomed accounts to 10000 (Bank Accounts) for same company
UPDATE child
SET ParentAccountId = parent.Id
OUTPUT
    inserted.CompanyId,
    inserted.Id,
    inserted.AccountNumber,
    deleted.ParentAccountId,
    inserted.ParentAccountId
INTO @Reparented (CompanyId, ChildCoaId, ChildAccountNumber, OldParentCoaId, NewParentCoaId)
FROM ChartOfAccounts child
INNER JOIN @ToDelete d ON d.CoaId = child.ParentAccountId
INNER JOIN ChartOfAccounts parent
    ON parent.CompanyId = child.CompanyId
   AND parent.AccountNumber = N'10000'
WHERE child.ParentAccountId <> parent.Id;

-- Delete Banks linked to target COA (no dependent rows expected after company reset)
DELETE b
OUTPUT
    deleted.CompanyId,
    deleted.Id,
    d.AccountNumber,
    deleted.BankName
INTO @DeletedBanks (CompanyId, BankId, AccountNumber, BankName)
FROM Banks b
INNER JOIN @ToDelete d ON d.CoaId = b.ChartOfAccountId;

-- Delete COA rows when no remaining FK blockers
DELETE coa
OUTPUT
    deleted.CompanyId,
    deleted.Id,
    deleted.AccountNumber,
    deleted.AccountName
INTO @DeletedCoa (CompanyId, CoaId, AccountNumber, AccountName)
FROM ChartOfAccounts coa
INNER JOIN @ToDelete d ON d.CoaId = coa.Id
WHERE NOT EXISTS (SELECT 1 FROM JournalEntryLines jel WHERE jel.ChartOfAccountId = coa.Id)
  AND NOT EXISTS (SELECT 1 FROM Banks b WHERE b.ChartOfAccountId = coa.Id)
  AND NOT EXISTS (SELECT 1 FROM ChartOfAccounts child WHERE child.ParentAccountId = coa.Id)
  AND NOT EXISTS (
      SELECT 1 FROM BankTransactions bt
      WHERE bt.ChartOfAccountId = coa.Id
         OR bt.CounterChartOfAccountId = coa.Id
         OR bt.TransferToChartOfAccountId = coa.Id);

-- Anything still in @ToDelete but not deleted => report as blocked
INSERT INTO @Blocked (CompanyId, CoaId, AccountNumber, AccountName, JeLineCount, Reason)
SELECT
    d.CompanyId,
    d.CoaId,
    d.AccountNumber,
    coa.AccountName,
    (SELECT COUNT(*) FROM JournalEntryLines jel WHERE jel.ChartOfAccountId = d.CoaId),
    N'Remaining FK or child references'
FROM @ToDelete d
INNER JOIN ChartOfAccounts coa ON coa.Id = d.CoaId
WHERE NOT EXISTS (SELECT 1 FROM @DeletedCoa x WHERE x.CoaId = d.CoaId)
  AND NOT EXISTS (SELECT 1 FROM @Blocked x WHERE x.CoaId = d.CoaId);

COMMIT TRANSACTION;

PRINT '=== Deleted Banks ===';
SELECT CompanyId, BankId, AccountNumber, BankName FROM @DeletedBanks ORDER BY CompanyId, AccountNumber;

PRINT '=== Reparented child COA ===';
SELECT * FROM @Reparented ORDER BY CompanyId, ChildAccountNumber;

PRINT '=== Deleted ChartOfAccounts ===';
SELECT CompanyId, CoaId, AccountNumber, AccountName FROM @DeletedCoa ORDER BY CompanyId, AccountNumber;

PRINT '=== Blocked (not deleted) ===';
SELECT CompanyId, CoaId, AccountNumber, AccountName, JeLineCount, Reason FROM @Blocked ORDER BY CompanyId, AccountNumber;

PRINT '=== Remaining bank-group COA (10000-10099) per company ===';
SELECT coa.CompanyId, coa.AccountNumber, coa.AccountName, coa.Id AS CoaId, coa.ParentAccountId
FROM ChartOfAccounts coa
WHERE coa.CompanyId IN (2, 4, 5, 6, 7)
  AND coa.AccountNumber >= N'10000'
  AND coa.AccountNumber < N'10100'
ORDER BY coa.CompanyId, coa.AccountNumber;

PRINT 'Delete company bank accounts completed.';
