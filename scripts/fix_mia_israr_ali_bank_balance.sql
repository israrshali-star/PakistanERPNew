/*
  MIA Company (Id 3): fix Muhammad Israr Ali / Meezan bank (COA 10008, Bank Id 12).

  Opening balance per QuickBooks ledger as of 31-May-2026: Rs 1,308,361.13 (debit).

  Safe to re-run.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @CompanyId INT = 3;
DECLARE @AccountNumber NVARCHAR(20) = N'10008';
DECLARE @CorrectOpening DECIMAL(18,2) = 1308361.13;
DECLARE @Now DATETIME2 = SYSUTCDATETIME();
DECLARE @User NVARCHAR(100) = N'bank-balance-fix';

DECLARE @CoaId INT;
DECLARE @BankId INT;

SELECT @CoaId = coa.Id
FROM ChartOfAccounts coa
WHERE coa.CompanyId = @CompanyId
  AND coa.IsDeleted = 0
  AND coa.AccountNumber = @AccountNumber;

SELECT @BankId = b.Id
FROM Banks b
WHERE b.CompanyId = @CompanyId
  AND b.IsDeleted = 0
  AND b.ChartOfAccountId = @CoaId;

IF @CoaId IS NULL OR @BankId IS NULL
BEGIN
    RAISERROR('Meezan bank COA or bank record not found.', 16, 1);
    RETURN;
END;

BEGIN TRANSACTION;

UPDATE coa
SET OpeningBalance = @CorrectOpening,
    UpdatedAt = @Now,
    UpdatedBy = @User
FROM ChartOfAccounts coa
WHERE coa.Id = @CoaId;

UPDATE cr
SET BankId = @BankId,
    UpdatedAt = @Now,
    UpdatedBy = @User
FROM CustomerReceipts cr
WHERE cr.CompanyId = @CompanyId
  AND cr.IsDeleted = 0
  AND cr.ReceiptNumber IN (N'RCP-0003', N'RCP-0004')
  AND cr.PaymentMethod = 3
  AND cr.BankId IS NULL;

DECLARE @GlDebits DECIMAL(18,2);
DECLARE @GlCredits DECIMAL(18,2);
DECLARE @GlClosing DECIMAL(18,2);

SELECT
    @GlDebits = ISNULL(SUM(jel.Debit), 0),
    @GlCredits = ISNULL(SUM(jel.Credit), 0)
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId
WHERE jel.ChartOfAccountId = @CoaId
  AND je.CompanyId = @CompanyId
  AND je.IsDeleted = 0
  AND je.Status = 2;

SET @GlClosing = @CorrectOpening + @GlDebits - @GlCredits;

UPDATE b
SET OpeningBalance = @CorrectOpening,
    CurrentBalance = @GlClosing,
    UpdatedAt = @Now,
    UpdatedBy = @User
FROM Banks b
WHERE b.Id = @BankId;

COMMIT TRANSACTION;

SELECT
    coa.AccountNumber,
    coa.AccountName,
    coa.OpeningBalance,
    @GlDebits AS GlDebits,
    @GlCredits AS GlCredits,
    @GlClosing AS GlClosingBalance,
    b.CurrentBalance AS BankCurrentBalance
FROM ChartOfAccounts coa
INNER JOIN Banks b ON b.ChartOfAccountId = coa.Id AND b.Id = @BankId
WHERE coa.Id = @CoaId;

SELECT cr.ReceiptNumber, cr.BankId, cr.Amount
FROM CustomerReceipts cr
WHERE cr.ReceiptNumber IN (N'RCP-0003', N'RCP-0004');
