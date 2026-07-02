-- Company 3: JE-1025 duplicated (double submit). Keep BankTxn 59 / JE 2997, soft-delete BankTxn 60 / JE 2996.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @Now DATETIME2 = SYSUTCDATETIME();
DECLARE @By NVARCHAR(200) = N'dedupe-je1025';
DECLARE @DupBankTxn INT = 60;
DECLARE @DupJournal INT = 2996;

PRINT '=== BEFORE (active JE-1025 rows) ===';
SELECT Id, EntryNumber, EntryDate, Description, Status, IsDeleted FROM JournalEntries WHERE EntryNumber=N'JE-1025' AND CompanyId=3;
SELECT Id, Amount, PartyName, IsDeleted FROM BankTransactions WHERE Id IN (59,60);

BEGIN TRAN;

UPDATE JournalEntries
SET IsDeleted=1, DeletedAt=@Now, DeletedBy=@By, UpdatedAt=@Now, UpdatedBy=@By
WHERE Id=@DupJournal AND IsDeleted=0;

UPDATE BankTransactions
SET IsDeleted=1, DeletedAt=@Now, DeletedBy=@By, UpdatedAt=@Now, UpdatedBy=@By
WHERE Id=@DupBankTxn AND IsDeleted=0;

COMMIT TRAN;

PRINT '=== AFTER (active JE-1025 rows) ===';
SELECT Id, EntryNumber, EntryDate, Description, Status, IsDeleted FROM JournalEntries WHERE EntryNumber=N'JE-1025' AND CompanyId=3;
SELECT Id, Amount, PartyName, IsDeleted FROM BankTransactions WHERE Id IN (59,60);

PRINT '=== Cash on Hand (10015) net from posted, non-deleted JEs ===';
SELECT SUM(l.Debit) AS Dr, SUM(l.Credit) AS Cr
FROM JournalEntryLines l
JOIN JournalEntries j ON j.Id=l.JournalEntryId
WHERE l.ChartOfAccountId=136 AND j.CompanyId=3 AND j.Status=2 AND j.IsDeleted=0;
