-- Delete test bank transactions for Company 3 (Arian Traders)
-- Amounts: Rs 800,000 (Deposit) and Rs 1,013,950 (Withdrawal)
-- These were entered without journal entries; only BankTransactions + Bank.CurrentBalance were affected.

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @CompanyId INT = 3;

BEGIN TRANSACTION;

-- Preview
SELECT bt.Id, bt.Amount, bt.TransactionType, bt.BankId, b.BankName, b.CurrentBalance
FROM BankTransactions bt
INNER JOIN Banks b ON b.Id = bt.BankId
WHERE bt.CompanyId = @CompanyId
  AND bt.IsDeleted = 0
  AND bt.Amount IN (800000.00, 1013950.00);

-- Reverse bank balance impacts before delete
UPDATE b
SET b.CurrentBalance = b.CurrentBalance - 800000.00,
    b.UpdatedAt = SYSUTCDATETIME(),
    b.UpdatedBy = N'system-cleanup'
FROM Banks b
INNER JOIN BankTransactions bt ON bt.BankId = b.Id
WHERE bt.CompanyId = @CompanyId
  AND bt.IsDeleted = 0
  AND bt.Amount = 800000.00
  AND bt.TransactionType = 1; -- Deposit

UPDATE b
SET b.CurrentBalance = b.CurrentBalance + 1013950.00,
    b.UpdatedAt = SYSUTCDATETIME(),
    b.UpdatedBy = N'system-cleanup'
FROM Banks b
INNER JOIN BankTransactions bt ON bt.BankId = b.Id
WHERE bt.CompanyId = @CompanyId
  AND bt.IsDeleted = 0
  AND bt.Amount = 1013950.00
  AND bt.TransactionType = 2; -- Withdrawal

-- Soft-delete any linked journal entries (none expected for these legacy rows)
UPDATE je
SET je.IsDeleted = 1,
    je.DeletedAt = SYSUTCDATETIME(),
    je.DeletedBy = N'system-cleanup'
FROM JournalEntries je
INNER JOIN BankTransactions bt ON bt.Id = je.ReferenceId
WHERE je.CompanyId = @CompanyId
  AND je.ReferenceType = N'BankTransaction'
  AND je.IsDeleted = 0
  AND bt.CompanyId = @CompanyId
  AND bt.IsDeleted = 0
  AND bt.Amount IN (800000.00, 1013950.00);

-- Soft-delete bank transactions
UPDATE bt
SET bt.IsDeleted = 1,
    bt.DeletedAt = SYSUTCDATETIME(),
    bt.DeletedBy = N'system-cleanup'
FROM BankTransactions bt
WHERE bt.CompanyId = @CompanyId
  AND bt.IsDeleted = 0
  AND bt.Amount IN (800000.00, 1013950.00);

COMMIT TRANSACTION;

-- Verify
SELECT COUNT(*) AS RemainingMatchingTransactions
FROM BankTransactions
WHERE CompanyId = @CompanyId
  AND IsDeleted = 0
  AND Amount IN (800000.00, 1013950.00);
