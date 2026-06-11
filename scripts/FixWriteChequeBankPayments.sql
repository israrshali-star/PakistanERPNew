/*
  Convert historical Write Cheque bank payments to direct bank-to-AR/AP transfers:
  - Clear cheque numbers on withdrawal bank transactions
  - Refresh journal descriptions and line memos

  Safe to re-run.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

BEGIN TRANSACTION;

UPDATE bt
SET ChequeNumber = NULL,
    ChequeDate = NULL
FROM BankTransactions bt
WHERE bt.IsDeleted = 0
  AND bt.TransactionType = 2;

DECLARE @ClearedChequeNumbers INT = @@ROWCOUNT;

UPDATE je
SET Description = N'Bank payment — ' + LTRIM(RTRIM(ISNULL(bt.PartyName, N'AR/AP')))
FROM JournalEntries je
INNER JOIN BankTransactions bt ON bt.JournalEntryId = je.Id AND bt.IsDeleted = 0
WHERE je.IsDeleted = 0
  AND bt.TransactionType = 2;

DECLARE @JournalDescriptionsUpdated INT = @@ROWCOUNT;

UPDATE jel
SET Memo = LTRIM(RTRIM(ISNULL(c.BuyerName, ISNULL(v.VendorName, bt.PartyName))))
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId AND je.IsDeleted = 0
INNER JOIN BankTransactions bt ON bt.JournalEntryId = je.Id AND bt.IsDeleted = 0
LEFT JOIN Customers c ON c.Id = bt.CustomerId AND c.CompanyId = bt.CompanyId AND c.IsDeleted = 0
LEFT JOIN Vendors v ON v.Id = bt.VendorId AND v.CompanyId = bt.CompanyId AND v.IsDeleted = 0
WHERE bt.TransactionType = 2
  AND jel.ChartOfAccountId = bt.CounterChartOfAccountId
  AND jel.Debit > 0;

DECLARE @ArApMemosUpdated INT = @@ROWCOUNT;

UPDATE jel
SET Memo = N'Bank payment — ' + LTRIM(RTRIM(ISNULL(c.BuyerName, ISNULL(v.VendorName, bt.PartyName))))
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId AND je.IsDeleted = 0
INNER JOIN BankTransactions bt ON bt.JournalEntryId = je.Id AND bt.IsDeleted = 0
LEFT JOIN Customers c ON c.Id = bt.CustomerId AND c.CompanyId = bt.CompanyId AND c.IsDeleted = 0
LEFT JOIN Vendors v ON v.Id = bt.VendorId AND v.CompanyId = bt.CompanyId AND v.IsDeleted = 0
WHERE bt.TransactionType = 2
  AND jel.ChartOfAccountId = bt.ChartOfAccountId
  AND jel.Credit > 0;

DECLARE @BankMemosUpdated INT = @@ROWCOUNT;

COMMIT TRANSACTION;

PRINT N'Cheque numbers cleared: ' + CAST(@ClearedChequeNumbers AS NVARCHAR(20));
PRINT N'Journal descriptions updated: ' + CAST(@JournalDescriptionsUpdated AS NVARCHAR(20));
PRINT N'AR/AP memos updated: ' + CAST(@ArApMemosUpdated AS NVARCHAR(20));
PRINT N'Bank memos updated: ' + CAST(@BankMemosUpdated AS NVARCHAR(20));

SELECT
    bt.Id,
    bt.CompanyId,
    bt.PartyName,
    bt.ChequeNumber,
    bt.Amount,
    je.EntryNumber
FROM BankTransactions bt
LEFT JOIN JournalEntries je ON je.Id = bt.JournalEntryId
WHERE bt.TransactionType = 2
  AND bt.IsDeleted = 0
  AND bt.CustomerId = 329
ORDER BY bt.Id;
