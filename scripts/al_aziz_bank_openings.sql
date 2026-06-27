-- Al-Aziz (company 2): QB 10001 -> ERP 10005, QB 10002 -> ERP 10004
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @CompanyId INT = 2;
DECLARE @Now DATETIME2 = SYSUTCDATETIME();

UPDATE coa
SET OpeningBalance = 54568.00,
    UpdatedAt = @Now,
    UpdatedBy = N'al-aziz-tb-import'
FROM ChartOfAccounts coa
WHERE coa.CompanyId = @CompanyId AND coa.IsDeleted = 0 AND coa.AccountNumber = N'10005';

UPDATE coa
SET OpeningBalance = 24382.40,
    UpdatedAt = @Now,
    UpdatedBy = N'al-aziz-tb-import'
FROM ChartOfAccounts coa
WHERE coa.CompanyId = @CompanyId AND coa.IsDeleted = 0 AND coa.AccountNumber = N'10004';

UPDATE b
SET OpeningBalance = coa.OpeningBalance,
    CurrentBalance = coa.OpeningBalance,
    UpdatedAt = @Now,
    UpdatedBy = N'al-aziz-tb-import'
FROM Banks b
INNER JOIN ChartOfAccounts coa ON coa.Id = b.ChartOfAccountId
WHERE b.CompanyId = @CompanyId AND b.IsDeleted = 0
  AND coa.AccountNumber IN (N'10004', N'10005');

SELECT AccountNumber, OpeningBalance
FROM ChartOfAccounts
WHERE CompanyId = @CompanyId AND IsDeleted = 0 AND AccountNumber IN (N'10004', N'10005');
