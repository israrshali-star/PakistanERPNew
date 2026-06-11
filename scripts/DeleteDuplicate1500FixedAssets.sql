/*
  Remove duplicate ChartOfAccounts row 1500 (Fixed Assets) for companies 2, 4, 5, 6, 7.
  Keeps the lowest Id per company; skips rows with journal/bank/child references.
  Company 3 is not modified. Safe to re-run.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

BEGIN TRANSACTION;

DECLARE @CompanyIds TABLE (CompanyId INT NOT NULL PRIMARY KEY);
INSERT INTO @CompanyIds (CompanyId) VALUES (2), (4), (5), (6), (7);

DECLARE @Keep TABLE (
    CompanyId INT NOT NULL PRIMARY KEY,
    KeepCoaId INT NOT NULL);

INSERT INTO @Keep (CompanyId, KeepCoaId)
SELECT coa.CompanyId, MIN(coa.Id)
FROM ChartOfAccounts coa
INNER JOIN @CompanyIds c ON c.CompanyId = coa.CompanyId
WHERE coa.AccountNumber = N'1500'
  AND coa.IsDeleted = 0
GROUP BY coa.CompanyId
HAVING COUNT(*) > 1;

DECLARE @ToDelete TABLE (
    CompanyId INT NOT NULL,
    CoaId INT NOT NULL,
    PRIMARY KEY (CompanyId, CoaId));

INSERT INTO @ToDelete (CompanyId, CoaId)
SELECT coa.CompanyId, coa.Id
FROM ChartOfAccounts coa
INNER JOIN @Keep k ON k.CompanyId = coa.CompanyId
WHERE coa.AccountNumber = N'1500'
  AND coa.IsDeleted = 0
  AND coa.Id <> k.KeepCoaId;

DECLARE @Blocked TABLE (
    CompanyId INT,
    CoaId INT,
    JeLines INT,
    Banks INT,
    BankTx INT,
    ChildCoa INT);

INSERT INTO @Blocked (CompanyId, CoaId, JeLines, Banks, BankTx, ChildCoa)
SELECT
    d.CompanyId,
    d.CoaId,
    (SELECT COUNT(*) FROM JournalEntryLines jel WHERE jel.ChartOfAccountId = d.CoaId),
    (SELECT COUNT(*) FROM Banks b WHERE b.ChartOfAccountId = d.CoaId),
    (SELECT COUNT(*) FROM BankTransactions bt
        WHERE bt.ChartOfAccountId = d.CoaId
           OR bt.CounterChartOfAccountId = d.CoaId
           OR bt.TransferToChartOfAccountId = d.CoaId),
    (SELECT COUNT(*) FROM ChartOfAccounts child WHERE child.ParentAccountId = d.CoaId)
FROM @ToDelete d;

DECLARE @Deleted TABLE (
    CompanyId INT,
    DeletedCoaId INT,
    KeptCoaId INT);

DELETE coa
OUTPUT d.CompanyId, deleted.Id, k.KeepCoaId INTO @Deleted (CompanyId, DeletedCoaId, KeptCoaId)
FROM ChartOfAccounts coa
INNER JOIN @ToDelete d ON d.CoaId = coa.Id
INNER JOIN @Keep k ON k.CompanyId = d.CompanyId
WHERE NOT EXISTS (
    SELECT 1 FROM @Blocked b
    WHERE b.CoaId = d.CoaId
      AND (b.JeLines > 0 OR b.Banks > 0 OR b.BankTx > 0 OR b.ChildCoa > 0));

COMMIT TRANSACTION;

PRINT '=== Kept 1500 Fixed Assets (per company) ===';
SELECT k.CompanyId, k.KeepCoaId AS KeptCoaId, coa.AccountNumber, coa.AccountName
FROM @Keep k
INNER JOIN ChartOfAccounts coa ON coa.Id = k.KeepCoaId
ORDER BY k.CompanyId;

PRINT '=== Deleted duplicate 1500 rows ===';
SELECT CompanyId, DeletedCoaId, KeptCoaId
FROM @Deleted
ORDER BY CompanyId, DeletedCoaId;

IF EXISTS (SELECT 1 FROM @Blocked WHERE JeLines > 0 OR Banks > 0 OR BankTx > 0 OR ChildCoa > 0)
BEGIN
    PRINT '=== Blocked (not deleted) ===';
    SELECT * FROM @Blocked
    WHERE JeLines > 0 OR Banks > 0 OR BankTx > 0 OR ChildCoa > 0;
END

PRINT '=== Verification: 1500 count per company (2,3,4,5,6,7) ===';
SELECT coa.CompanyId, COUNT(*) AS RowCount1500
FROM ChartOfAccounts coa
WHERE coa.CompanyId IN (2, 3, 4, 5, 6, 7)
  AND coa.AccountNumber = N'1500'
  AND coa.IsDeleted = 0
GROUP BY coa.CompanyId
ORDER BY coa.CompanyId;
