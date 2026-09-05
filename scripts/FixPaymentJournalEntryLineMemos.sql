/*
  Backfill JournalEntryLine.Memo for company 3 so chart-of-account ledgers
  show transaction notes on the relevant cash, bank, and party accounts.

  Ledger display (ChartOfAccountsService.GetLedgerAsync) prefers line.Memo over
  journal entry description.

  Rules:
    Customer receipt — keep existing party/document memo and append Notes
    Bank transaction — append Description (write cheque / transfer / deposit notes)
    Make Deposit     — bank + undeposited lines use customer, cheque, and receipt notes

  Safe to re-run: only appends notes that are not already present.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @CompanyId INT = 3;

BEGIN TRANSACTION;

-- Customer receipts: write Notes on every journal line (cash, bank, AR)
UPDATE jel
SET Memo = CASE
    WHEN cr.Notes IS NULL OR LTRIM(RTRIM(cr.Notes)) = N'' THEN jel.Memo
    WHEN jel.Memo IS NULL OR LTRIM(RTRIM(jel.Memo)) = N'' THEN LTRIM(RTRIM(cr.Notes))
    WHEN CHARINDEX(LTRIM(RTRIM(cr.Notes)), jel.Memo) > 0 THEN jel.Memo
    ELSE jel.Memo + N' — ' + LTRIM(RTRIM(cr.Notes))
END
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId
INNER JOIN CustomerReceipts cr ON cr.Id = je.ReferenceId AND cr.CompanyId = je.CompanyId
WHERE je.CompanyId = @CompanyId
  AND je.IsDeleted = 0
  AND je.ReferenceType = N'CustomerReceipt'
  AND cr.IsDeleted = 0;

DECLARE @CustomerReceiptLinesUpdated INT = @@ROWCOUNT;

-- Banking write cheque / transfer / deposit: write Description on every line
UPDATE jel
SET Memo = CASE
    WHEN bt.Description IS NULL OR LTRIM(RTRIM(bt.Description)) = N'' THEN jel.Memo
    WHEN jel.Memo IS NULL OR LTRIM(RTRIM(jel.Memo)) = N'' THEN LTRIM(RTRIM(bt.Description))
    WHEN CHARINDEX(LTRIM(RTRIM(bt.Description)), jel.Memo) > 0 THEN jel.Memo
    ELSE jel.Memo + N' — ' + LTRIM(RTRIM(bt.Description))
END
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId
INNER JOIN BankTransactions bt ON bt.Id = je.ReferenceId AND bt.CompanyId = je.CompanyId
WHERE je.CompanyId = @CompanyId
  AND je.IsDeleted = 0
  AND je.ReferenceType = N'BankTransaction'
  AND bt.IsDeleted = 0;

DECLARE @BankTransactionLinesUpdated INT = @@ROWCOUNT;

-- Make Deposit: put the deposited cash-receipt note on bank + undeposited lines
UPDATE jel
SET Memo = CASE
    WHEN CHARINDEX(LTRIM(RTRIM(deposit.Memo)), ISNULL(jel.Memo, N'')) > 0
         AND (bt.Description IS NULL OR LTRIM(RTRIM(bt.Description)) = N''
              OR CHARINDEX(LTRIM(RTRIM(bt.Description)), ISNULL(jel.Memo, N'')) > 0)
        THEN jel.Memo
    ELSE CASE
        WHEN bt.Description IS NULL OR LTRIM(RTRIM(bt.Description)) = N''
             OR CHARINDEX(LTRIM(RTRIM(bt.Description)), deposit.Memo) > 0
            THEN deposit.Memo
        ELSE deposit.Memo + N' — ' + LTRIM(RTRIM(bt.Description))
    END
END
FROM JournalEntryLines jel
INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId
INNER JOIN BankTransactions bt ON bt.Id = je.ReferenceId AND bt.CompanyId = je.CompanyId
INNER JOIN (
    SELECT
        cr.DepositedBankTransactionId,
        CASE
            WHEN cr.PaymentMethod = 2 AND cr.ChequeNumber IS NOT NULL AND LTRIM(RTRIM(cr.ChequeNumber)) <> N''
                THEN LTRIM(RTRIM(c.BuyerName)) + N' — Chq #' + LTRIM(RTRIM(cr.ChequeNumber))
            ELSE LTRIM(RTRIM(c.BuyerName)) + N' — ' + LTRIM(RTRIM(cr.ReceiptNumber))
        END
        + CASE
            WHEN cr.Notes IS NULL OR LTRIM(RTRIM(cr.Notes)) = N'' THEN N''
            ELSE N' — ' + LTRIM(RTRIM(cr.Notes))
          END AS Memo
    FROM CustomerReceipts cr
    INNER JOIN Customers c ON c.Id = cr.CustomerId AND c.CompanyId = cr.CompanyId
    WHERE cr.CompanyId = @CompanyId
      AND cr.IsDeleted = 0
      AND c.IsDeleted = 0
      AND cr.DepositedBankTransactionId IS NOT NULL
) deposit ON deposit.DepositedBankTransactionId = bt.Id
WHERE je.CompanyId = @CompanyId
  AND je.IsDeleted = 0
  AND je.ReferenceType = N'BankTransaction'
  AND bt.IsDeleted = 0
  AND bt.TransactionType = 1;

DECLARE @DepositLinesUpdated INT = @@ROWCOUNT;

-- Journal header description: include receipt notes for list/search clarity
UPDATE je
SET Description = CASE
    WHEN CHARINDEX(LTRIM(RTRIM(cr.Notes)), ISNULL(je.Description, N'')) > 0
        THEN je.Description
    WHEN je.Description IS NULL OR LTRIM(RTRIM(je.Description)) = N''
        THEN N'Customer receipt ' + LTRIM(RTRIM(cr.ReceiptNumber)) + N' — ' + LTRIM(RTRIM(cr.Notes))
    ELSE je.Description + N' — ' + LTRIM(RTRIM(cr.Notes))
END
FROM JournalEntries je
INNER JOIN CustomerReceipts cr ON cr.Id = je.ReferenceId AND cr.CompanyId = je.CompanyId
WHERE je.CompanyId = @CompanyId
  AND je.IsDeleted = 0
  AND je.ReferenceType = N'CustomerReceipt'
  AND cr.IsDeleted = 0
  AND cr.Notes IS NOT NULL
  AND LTRIM(RTRIM(cr.Notes)) <> N'';

DECLARE @ReceiptDescriptionsUpdated INT = @@ROWCOUNT;

UPDATE je
SET Description = CASE
    WHEN CHARINDEX(LTRIM(RTRIM(bt.Description)), ISNULL(je.Description, N'')) > 0
        THEN je.Description
    WHEN je.Description IS NULL OR LTRIM(RTRIM(je.Description)) = N''
        THEN LTRIM(RTRIM(bt.Description))
    ELSE je.Description + N' — ' + LTRIM(RTRIM(bt.Description))
END
FROM JournalEntries je
INNER JOIN BankTransactions bt ON bt.Id = je.ReferenceId AND bt.CompanyId = je.CompanyId
WHERE je.CompanyId = @CompanyId
  AND je.IsDeleted = 0
  AND je.ReferenceType = N'BankTransaction'
  AND bt.IsDeleted = 0
  AND bt.Description IS NOT NULL
  AND LTRIM(RTRIM(bt.Description)) <> N'';

DECLARE @BankDescriptionsUpdated INT = @@ROWCOUNT;

COMMIT TRANSACTION;

PRINT CONCAT('Customer receipt JE lines updated: ', @CustomerReceiptLinesUpdated);
PRINT CONCAT('Bank transaction JE lines updated: ', @BankTransactionLinesUpdated);
PRINT CONCAT('Deposit receipt memos updated: ', @DepositLinesUpdated);
PRINT CONCAT('Customer receipt JE descriptions updated: ', @ReceiptDescriptionsUpdated);
PRINT CONCAT('Bank transaction JE descriptions updated: ', @BankDescriptionsUpdated);
