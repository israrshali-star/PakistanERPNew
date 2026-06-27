-- Kashaf Polyester (company 5): QB 10001 -> ERP 10007, QB 10002 -> ERP 10015
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @CompanyId INT = 5;
DECLARE @Now DATETIME2 = SYSUTCDATETIME();

UPDATE coa
SET OpeningBalance = 172129.00,
    UpdatedAt = @Now,
    UpdatedBy = N'kashaf-tb-import'
FROM ChartOfAccounts coa
WHERE coa.CompanyId = @CompanyId
  AND coa.IsDeleted = 0
  AND coa.AccountNumber = N'10007';

UPDATE coa
SET OpeningBalance = 2351657.51,
    UpdatedAt = @Now,
    UpdatedBy = N'kashaf-tb-import'
FROM ChartOfAccounts coa
WHERE coa.CompanyId = @CompanyId
  AND coa.IsDeleted = 0
  AND coa.AccountNumber = N'10015';

UPDATE b
SET OpeningBalance = coa.OpeningBalance,
    CurrentBalance = coa.OpeningBalance,
    UpdatedAt = @Now,
    UpdatedBy = N'kashaf-tb-import'
FROM Banks b
INNER JOIN ChartOfAccounts coa ON coa.Id = b.ChartOfAccountId
WHERE b.CompanyId = @CompanyId
  AND b.IsDeleted = 0
  AND coa.AccountNumber IN (N'10007', N'10015');

SELECT AccountNumber, OpeningBalance
FROM ChartOfAccounts
WHERE CompanyId = @CompanyId AND IsDeleted = 0 AND AccountNumber IN (N'10007', N'10015');
