-- Revert the JE-1025 duplicate soft-delete: restore BankTxn 60 / JE 2996.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @Now DATETIME2 = SYSUTCDATETIME();
DECLARE @By NVARCHAR(200) = N'revert-dedupe-je1025';

BEGIN TRAN;

UPDATE JournalEntries
SET IsDeleted=0, DeletedAt=NULL, DeletedBy=NULL, UpdatedAt=@Now, UpdatedBy=@By
WHERE Id=2996;

UPDATE BankTransactions
SET IsDeleted=0, DeletedAt=NULL, DeletedBy=NULL, UpdatedAt=@Now, UpdatedBy=@By
WHERE Id=60;

COMMIT TRAN;

PRINT '=== AFTER revert (JE-1025 rows) ===';
SELECT Id, EntryNumber, EntryDate, Description, Status, IsDeleted FROM JournalEntries WHERE EntryNumber=N'JE-1025' AND CompanyId=3;
SELECT Id, Amount, PartyName, IsDeleted FROM BankTransactions WHERE Id IN (59,60);
