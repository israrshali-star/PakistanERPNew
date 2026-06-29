-- AP credits net vendor liability; W/H tax posts to account 12810 (not AP).
-- Companies 2, 4, 5, 6, 7.

BEGIN TRANSACTION;

UPDATE jel
SET jel.Credit = vb.NetAmount
FROM JournalEntryLines jel
JOIN VendorBills vb ON vb.JournalEntryId = jel.JournalEntryId
WHERE vb.CompanyId IN (2, 4, 5, 6, 7)
  AND vb.Status = 2
  AND vb.IsDeleted = 0
  AND jel.Memo = 'Account Payable';

INSERT INTO JournalEntryLines (JournalEntryId, ChartOfAccountId, Debit, Credit, Memo)
SELECT
    vb.JournalEntryId,
    wht.Id,
    0,
    vb.WithholdingTaxAmount,
    'W/H Tax u/s 153(1)(a)'
FROM VendorBills vb
JOIN ChartOfAccounts wht ON wht.CompanyId = vb.CompanyId AND wht.AccountNumber = '12810'
WHERE vb.CompanyId IN (2, 4, 5, 6, 7)
  AND vb.Status = 2
  AND vb.IsDeleted = 0
  AND vb.WithholdingTaxAmount > 0
  AND NOT EXISTS (
      SELECT 1
      FROM JournalEntryLines existing
      WHERE existing.JournalEntryId = vb.JournalEntryId
        AND existing.ChartOfAccountId = wht.Id);

COMMIT TRANSACTION;
