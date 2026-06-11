/*
  Set Banks.NextChequeNumber from the last posted write cheque per bank COA account.
  Safe to re-run.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

BEGIN TRANSACTION;

;WITH LastCheque AS (
    SELECT
        bt.CompanyId,
        bt.ChartOfAccountId,
        LTRIM(RTRIM(bt.ChequeNumber)) AS ChequeNumber,
        ROW_NUMBER() OVER (
            PARTITION BY bt.CompanyId, bt.ChartOfAccountId
            ORDER BY bt.Id DESC
        ) AS RowNum
    FROM BankTransactions bt
    WHERE bt.IsDeleted = 0
      AND bt.TransactionType = 2
      AND bt.ChequeNumber IS NOT NULL
      AND LTRIM(RTRIM(bt.ChequeNumber)) <> N''
)
UPDATE b
SET NextChequeNumber = CASE
    WHEN TRY_CAST(lc.ChequeNumber AS BIGINT) IS NOT NULL
        THEN RIGHT(
            REPLICATE(N'0', LEN(lc.ChequeNumber))
                + CAST(TRY_CAST(lc.ChequeNumber AS BIGINT) + 1 AS NVARCHAR(50)),
            LEN(lc.ChequeNumber))
    ELSE lc.ChequeNumber
END,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = N'system-backfill'
FROM Banks b
INNER JOIN LastCheque lc
    ON lc.CompanyId = b.CompanyId
   AND lc.ChartOfAccountId = b.ChartOfAccountId
   AND lc.RowNum = 1
WHERE b.IsDeleted = 0
  AND b.ChartOfAccountId IS NOT NULL
  AND (b.NextChequeNumber IS NULL OR LTRIM(RTRIM(b.NextChequeNumber)) = N'');

DECLARE @Updated INT = @@ROWCOUNT;

COMMIT TRANSACTION;

PRINT N'Banks updated with next cheque number: ' + CAST(@Updated AS NVARCHAR(20));

SELECT
    b.CompanyId,
    coa.AccountNumber,
    b.BankName,
    b.NextChequeNumber
FROM Banks b
LEFT JOIN ChartOfAccounts coa ON coa.Id = b.ChartOfAccountId
WHERE b.IsDeleted = 0
  AND b.NextChequeNumber IS NOT NULL
ORDER BY b.CompanyId, coa.AccountNumber;
