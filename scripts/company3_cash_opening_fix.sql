-- Company 3: set Cash on Hand (10015) opening balance to 45,440.77 as of 31/05/2026.
-- Offset the 1,075,469.00 difference to Opening Balance Equity (30000) so the trial balance stays balanced.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @CompanyId INT = 3;
DECLARE @Now DATETIME2 = SYSUTCDATETIME();
DECLARE @By NVARCHAR(200) = N'cash-opening-fix-2026-05-31';

DECLARE @NewCash DECIMAL(18,2) = 45440.77;

DECLARE @CashId INT  = (SELECT Id FROM ChartOfAccounts WHERE CompanyId=@CompanyId AND AccountNumber=N'10015' AND IsDeleted=0);
DECLARE @ObeId  INT  = (SELECT Id FROM ChartOfAccounts WHERE CompanyId=@CompanyId AND AccountNumber=N'30000' AND IsDeleted=0);
DECLARE @OldCash DECIMAL(18,2) = (SELECT OpeningBalance FROM ChartOfAccounts WHERE Id=@CashId);
DECLARE @Delta   DECIMAL(18,2) = @OldCash - @NewCash;  -- amount removed from cash

PRINT '=== BEFORE ===';
SELECT AccountNumber, AccountName, OpeningBalance FROM ChartOfAccounts WHERE Id IN (@CashId,@ObeId);
SELECT Id, BankName, OpeningBalance, CurrentBalance FROM Banks WHERE CompanyId=@CompanyId AND ChartOfAccountId=@CashId AND IsDeleted=0;

BEGIN TRAN;

UPDATE ChartOfAccounts SET OpeningBalance=@NewCash, UpdatedAt=@Now, UpdatedBy=@By WHERE Id=@CashId;
UPDATE ChartOfAccounts SET OpeningBalance=OpeningBalance + @Delta, UpdatedAt=@Now, UpdatedBy=@By WHERE Id=@ObeId;

UPDATE Banks SET OpeningBalance=@NewCash, CurrentBalance=CurrentBalance - @Delta, UpdatedAt=@Now, UpdatedBy=@By
WHERE CompanyId=@CompanyId AND ChartOfAccountId=@CashId AND IsDeleted=0;

COMMIT TRAN;

PRINT '=== AFTER ===';
SELECT AccountNumber, AccountName, OpeningBalance FROM ChartOfAccounts WHERE Id IN (@CashId,@ObeId);
SELECT Id, BankName, OpeningBalance, CurrentBalance FROM Banks WHERE CompanyId=@CompanyId AND ChartOfAccountId=@CashId AND IsDeleted=0;
PRINT '=== DELTA MOVED TO OBE ===';
SELECT @Delta AS DeltaMovedFromCashToObe;
