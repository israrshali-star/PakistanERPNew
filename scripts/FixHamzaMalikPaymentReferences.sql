/*
  Restore Hamza Malik write-cheque payment references:
    PAY-0004 (Id 4)  -> Cheque #50052468
    PAY-0005 (Id 5)  -> Cheque #50052470
    PAY-0007 (Id 7)  -> Cheque #50052471
    PAY-0008 (Id 8)  -> Bank Transfer

  Safe to re-run.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

BEGIN TRANSACTION;

UPDATE BankTransactions
SET PaymentMethod = 2,
    ChequeNumber = N'50052468',
    ChequeDate = TransactionDate
WHERE Id = 4 AND CompanyId = 3 AND CustomerId = 329;

UPDATE BankTransactions
SET PaymentMethod = 2,
    ChequeNumber = N'50052470',
    ChequeDate = TransactionDate
WHERE Id = 5 AND CompanyId = 3 AND CustomerId = 329;

UPDATE BankTransactions
SET PaymentMethod = 2,
    ChequeNumber = N'50052471',
    ChequeDate = TransactionDate
WHERE Id = 7 AND CompanyId = 3 AND CustomerId = 329;

UPDATE BankTransactions
SET PaymentMethod = 3,
    ChequeNumber = NULL,
    ChequeDate = NULL
WHERE Id = 8 AND CompanyId = 3 AND CustomerId = 329;

UPDATE je
SET Description = N'Cheque — Hamza Malik'
FROM JournalEntries je
INNER JOIN BankTransactions bt ON bt.JournalEntryId = je.Id
WHERE bt.Id IN (4, 5, 7) AND je.IsDeleted = 0;

UPDATE je
SET Description = N'Bank transfer — Hamza Malik'
FROM JournalEntries je
INNER JOIN BankTransactions bt ON bt.JournalEntryId = je.Id
WHERE bt.Id = 8 AND je.IsDeleted = 0;

UPDATE jel
SET Memo = N'Hamza Malik'
FROM JournalEntryLines jel
INNER JOIN BankTransactions bt ON bt.JournalEntryId = jel.JournalEntryId
WHERE bt.Id IN (4, 5, 7, 8)
  AND jel.ChartOfAccountId = bt.CounterChartOfAccountId
  AND jel.Debit > 0;

UPDATE jel
SET Memo = N'Hamza Malik — Chq #50052468'
FROM JournalEntryLines jel
INNER JOIN BankTransactions bt ON bt.JournalEntryId = jel.JournalEntryId
WHERE bt.Id = 4
  AND jel.ChartOfAccountId = bt.ChartOfAccountId
  AND jel.Credit > 0;

UPDATE jel
SET Memo = N'Hamza Malik — Chq #50052470'
FROM JournalEntryLines jel
INNER JOIN BankTransactions bt ON bt.JournalEntryId = jel.JournalEntryId
WHERE bt.Id = 5
  AND jel.ChartOfAccountId = bt.ChartOfAccountId
  AND jel.Credit > 0;

UPDATE jel
SET Memo = N'Hamza Malik — Chq #50052471'
FROM JournalEntryLines jel
INNER JOIN BankTransactions bt ON bt.JournalEntryId = jel.JournalEntryId
WHERE bt.Id = 7
  AND jel.ChartOfAccountId = bt.ChartOfAccountId
  AND jel.Credit > 0;

UPDATE jel
SET Memo = N'Hamza Malik — Bank transfer'
FROM JournalEntryLines jel
INNER JOIN BankTransactions bt ON bt.JournalEntryId = jel.JournalEntryId
WHERE bt.Id = 8
  AND jel.ChartOfAccountId = bt.ChartOfAccountId
  AND jel.Credit > 0;

UPDATE b
SET NextChequeNumber = N'50052472',
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = N'system-fix'
FROM Banks b
INNER JOIN ChartOfAccounts coa ON coa.Id = b.ChartOfAccountId
WHERE b.CompanyId = 3
  AND coa.AccountNumber = N'10008'
  AND b.IsDeleted = 0;

COMMIT TRANSACTION;

SELECT
    bt.Id,
    bt.PaymentMethod,
    bt.ChequeNumber,
    bt.Amount,
    je.EntryNumber,
    je.Description
FROM BankTransactions bt
LEFT JOIN JournalEntries je ON je.Id = bt.JournalEntryId
WHERE bt.Id IN (4, 5, 7, 8)
ORDER BY bt.Id;

SELECT
    je.EntryNumber,
    coa.AccountNumber,
    jel.Debit,
    jel.Credit,
    jel.Memo
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId
INNER JOIN ChartOfAccounts coa ON coa.Id = jel.ChartOfAccountId
WHERE je.Id IN (457, 458, 459, 463)
ORDER BY je.EntryNumber, jel.Id;
