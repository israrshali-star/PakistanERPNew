-- Arian Traders (company 7): QB 10110 -> ERP 10001, QB 10120 -> ERP 10009
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @CompanyId INT = 7;
DECLARE @Now DATETIME2 = SYSUTCDATETIME();

UPDATE coa SET OpeningBalance = 29776.00, UpdatedAt = @Now, UpdatedBy = N'arian-tb-import'
FROM ChartOfAccounts coa WHERE coa.CompanyId = @CompanyId AND coa.IsDeleted = 0 AND coa.AccountNumber = N'10001';

UPDATE coa SET OpeningBalance = 58320.00, UpdatedAt = @Now, UpdatedBy = N'arian-tb-import'
FROM ChartOfAccounts coa WHERE coa.CompanyId = @CompanyId AND coa.IsDeleted = 0 AND coa.AccountNumber = N'10009';

UPDATE b SET OpeningBalance = coa.OpeningBalance, CurrentBalance = coa.OpeningBalance, UpdatedAt = @Now, UpdatedBy = N'arian-tb-import'
FROM Banks b INNER JOIN ChartOfAccounts coa ON coa.Id = b.ChartOfAccountId
WHERE b.CompanyId = @CompanyId AND b.IsDeleted = 0 AND coa.AccountNumber IN (N'10001', N'10009');

SELECT AccountNumber, OpeningBalance FROM ChartOfAccounts
WHERE CompanyId = @CompanyId AND IsDeleted = 0 AND AccountNumber IN (N'10001', N'10009', N'10015');
