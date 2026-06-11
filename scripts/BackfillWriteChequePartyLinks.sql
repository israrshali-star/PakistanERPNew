/*
  Link existing Write Cheque (Withdrawal) bank transactions to Customers/Vendors
  and refresh journal line memos so customer/vendor ledgers and AR/AP statements
  show the party name.

  Safe to re-run.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

BEGIN TRANSACTION;

-- Match cheques to customers by PartyName within the same company
UPDATE bt
SET CustomerId = c.Id
FROM BankTransactions bt
INNER JOIN Customers c
    ON c.CompanyId = bt.CompanyId
   AND c.IsDeleted = 0
   AND LTRIM(RTRIM(c.BuyerName)) = LTRIM(RTRIM(bt.PartyName))
WHERE bt.IsDeleted = 0
  AND bt.TransactionType = 2
  AND bt.CustomerId IS NULL
  AND bt.VendorId IS NULL
  AND bt.PartyName IS NOT NULL
  AND LTRIM(RTRIM(bt.PartyName)) <> N'';

DECLARE @CustomerLinks INT = @@ROWCOUNT;

-- Match remaining cheques to vendors by PartyName
UPDATE bt
SET VendorId = v.Id
FROM BankTransactions bt
INNER JOIN Vendors v
    ON v.CompanyId = bt.CompanyId
   AND v.IsDeleted = 0
   AND LTRIM(RTRIM(v.VendorName)) = LTRIM(RTRIM(bt.PartyName))
WHERE bt.IsDeleted = 0
  AND bt.TransactionType = 2
  AND bt.CustomerId IS NULL
  AND bt.VendorId IS NULL
  AND bt.PartyName IS NOT NULL
  AND LTRIM(RTRIM(bt.PartyName)) <> N'';

DECLARE @VendorLinks INT = @@ROWCOUNT;

-- AR debit line memo = customer name; AP debit line memo = vendor name
UPDATE jel
SET Memo = CASE
    WHEN bt.CustomerId IS NOT NULL THEN LTRIM(RTRIM(c.BuyerName))
    WHEN bt.VendorId IS NOT NULL THEN LTRIM(RTRIM(v.VendorName))
    ELSE jel.Memo
END
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId AND je.IsDeleted = 0
INNER JOIN BankTransactions bt ON bt.JournalEntryId = je.Id AND bt.IsDeleted = 0
LEFT JOIN Customers c ON c.Id = bt.CustomerId AND c.CompanyId = bt.CompanyId AND c.IsDeleted = 0
LEFT JOIN Vendors v ON v.Id = bt.VendorId AND v.CompanyId = bt.CompanyId AND v.IsDeleted = 0
WHERE bt.TransactionType = 2
  AND jel.ChartOfAccountId = bt.CounterChartOfAccountId
  AND jel.Debit > 0
  AND (bt.CustomerId IS NOT NULL OR bt.VendorId IS NOT NULL);

DECLARE @MemoLinesUpdated INT = @@ROWCOUNT;

-- Bank credit line memo = party — Chq #number (when cheque number present)
UPDATE jel
SET Memo = CASE
    WHEN bt.CustomerId IS NOT NULL AND NULLIF(LTRIM(RTRIM(bt.ChequeNumber)), N'') IS NOT NULL
        THEN LTRIM(RTRIM(c.BuyerName)) + N' — Chq #' + LTRIM(RTRIM(bt.ChequeNumber))
    WHEN bt.VendorId IS NOT NULL AND NULLIF(LTRIM(RTRIM(bt.ChequeNumber)), N'') IS NOT NULL
        THEN LTRIM(RTRIM(v.VendorName)) + N' — Chq #' + LTRIM(RTRIM(bt.ChequeNumber))
    WHEN bt.CustomerId IS NOT NULL THEN LTRIM(RTRIM(c.BuyerName))
    WHEN bt.VendorId IS NOT NULL THEN LTRIM(RTRIM(v.VendorName))
    ELSE jel.Memo
END
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId AND je.IsDeleted = 0
INNER JOIN BankTransactions bt ON bt.JournalEntryId = je.Id AND bt.IsDeleted = 0
LEFT JOIN Customers c ON c.Id = bt.CustomerId AND c.CompanyId = bt.CompanyId AND c.IsDeleted = 0
LEFT JOIN Vendors v ON v.Id = bt.VendorId AND v.CompanyId = bt.CompanyId AND v.IsDeleted = 0
WHERE bt.TransactionType = 2
  AND jel.ChartOfAccountId = bt.ChartOfAccountId
  AND jel.Credit > 0
  AND (bt.CustomerId IS NOT NULL OR bt.VendorId IS NOT NULL);

DECLARE @BankMemoLinesUpdated INT = @@ROWCOUNT;

COMMIT TRANSACTION;

PRINT N'Customer links: ' + CAST(@CustomerLinks AS NVARCHAR(20));
PRINT N'Vendor links: ' + CAST(@VendorLinks AS NVARCHAR(20));
PRINT N'AR/AP memo lines updated: ' + CAST(@MemoLinesUpdated AS NVARCHAR(20));
PRINT N'Bank memo lines updated: ' + CAST(@BankMemoLinesUpdated AS NVARCHAR(20));

-- Verification: Hamza Malik cheque (Company 3)
SELECT
    bt.Id,
    bt.CompanyId,
    bt.PartyName,
    bt.CustomerId,
    c.BuyerName,
    bt.Amount,
    bt.ChequeNumber,
    je.EntryNumber
FROM BankTransactions bt
LEFT JOIN Customers c ON c.Id = bt.CustomerId
LEFT JOIN JournalEntries je ON je.Id = bt.JournalEntryId
WHERE bt.TransactionType = 2
  AND bt.IsDeleted = 0
  AND (
      bt.PartyName LIKE N'%Hamza%Malik%'
      OR c.BuyerName LIKE N'%Hamza%Malik%'
  );
