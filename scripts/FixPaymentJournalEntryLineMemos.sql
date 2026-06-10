/*
  Backfill JournalEntryLine.Memo for customer receipts and vendor payments so
  chart-of-account ledgers show the party name (customer/vendor) instead of
  generic bank/cash labels.

  Ledger display (ChartOfAccountsService.GetLedgerAsync) prefers line.Memo over
  journal entry description.

  Rules:
    Customer receipt — bank debit line:  {BuyerName} — {ReceiptNumber}
    Customer receipt — cash debit (10015): {BuyerName}
    Customer receipt — AR credit line:   {BuyerName}

    Vendor payment — bank credit line:   {VendorName} — {PaymentNumber}
    Vendor payment — cash credit (10015): {VendorName}
    Vendor payment — AP debit line:      {VendorName}

  Safe to re-run: sets Memo to the target value each time.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @CompanyId INT = 3;

DECLARE @CashAccountId INT;
DECLARE @ArAccountId INT;
DECLARE @ApAccountId INT;

SELECT @CashAccountId = Id
FROM ChartOfAccounts
WHERE CompanyId = @CompanyId
  AND AccountNumber = N'10015'
  AND IsDeleted = 0;

SELECT @ArAccountId = Id
FROM ChartOfAccounts
WHERE CompanyId = @CompanyId
  AND AccountNumber = N'11110'
  AND IsDeleted = 0;

SELECT @ApAccountId = Id
FROM ChartOfAccounts
WHERE CompanyId = @CompanyId
  AND AccountNumber = N'20000'
  AND IsDeleted = 0;

IF @CashAccountId IS NULL OR @ArAccountId IS NULL OR @ApAccountId IS NULL
BEGIN
    RAISERROR('Required chart of accounts (10015, 11110, 20000) not found for CompanyId %d.', 16, 1, @CompanyId);
    RETURN;
END;

BEGIN TRANSACTION;

-- Customer receipts
UPDATE jel
SET Memo = CASE
    WHEN jel.ChartOfAccountId = @ArAccountId THEN LTRIM(RTRIM(c.BuyerName))
    WHEN jel.ChartOfAccountId = @CashAccountId AND jel.Debit > 0 THEN LTRIM(RTRIM(c.BuyerName))
    WHEN jel.Debit > 0 THEN LTRIM(RTRIM(c.BuyerName)) + N' — ' + LTRIM(RTRIM(cr.ReceiptNumber))
    ELSE jel.Memo
END
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId
INNER JOIN CustomerReceipts cr ON cr.Id = je.ReferenceId AND cr.CompanyId = je.CompanyId
INNER JOIN Customers c ON c.Id = cr.CustomerId AND c.CompanyId = cr.CompanyId
WHERE je.CompanyId = @CompanyId
  AND je.IsDeleted = 0
  AND je.ReferenceType = N'CustomerReceipt'
  AND cr.IsDeleted = 0
  AND c.IsDeleted = 0;

DECLARE @CustomerReceiptLinesUpdated INT = @@ROWCOUNT;

-- Vendor payments
UPDATE jel
SET Memo = CASE
    WHEN jel.ChartOfAccountId = @ApAccountId THEN LTRIM(RTRIM(v.VendorName))
    WHEN jel.ChartOfAccountId = @CashAccountId AND jel.Credit > 0 THEN LTRIM(RTRIM(v.VendorName))
    WHEN jel.Credit > 0 THEN LTRIM(RTRIM(v.VendorName)) + N' — ' + LTRIM(RTRIM(vp.PaymentNumber))
    ELSE jel.Memo
END
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId
INNER JOIN VendorPayments vp ON vp.Id = je.ReferenceId AND vp.CompanyId = je.CompanyId
INNER JOIN Vendors v ON v.Id = vp.VendorId AND v.CompanyId = vp.CompanyId
WHERE je.CompanyId = @CompanyId
  AND je.IsDeleted = 0
  AND je.ReferenceType = N'VendorPayment'
  AND vp.IsDeleted = 0
  AND v.IsDeleted = 0;

DECLARE @VendorPaymentLinesUpdated INT = @@ROWCOUNT;

COMMIT TRANSACTION;

PRINT CONCAT('Customer receipt JE lines updated: ', @CustomerReceiptLinesUpdated);
PRINT CONCAT('Vendor payment JE lines updated: ', @VendorPaymentLinesUpdated);

-- Sample verification
SELECT TOP 12
    je.EntryNumber,
    je.Description,
    coa.AccountNumber,
    coa.AccountName,
    jel.Debit,
    jel.Credit,
    jel.Memo
FROM JournalEntries je
INNER JOIN JournalEntryLines jel ON jel.JournalEntryId = je.Id
INNER JOIN ChartOfAccounts coa ON coa.Id = jel.ChartOfAccountId
WHERE je.CompanyId = @CompanyId
  AND je.IsDeleted = 0
  AND je.ReferenceType IN (N'CustomerReceipt', N'VendorPayment')
ORDER BY je.Id, jel.Id;
