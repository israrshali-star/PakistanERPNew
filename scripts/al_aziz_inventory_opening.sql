-- Align inventory opening (12110) to QB valuation total; OBE replugged separately.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @CompanyId INT = 2;
DECLARE @Now DATETIME2 = SYSUTCDATETIME();
DECLARE @InventoryOpening DECIMAL(18,2) = 5670757.56;

UPDATE coa
SET OpeningBalance = @InventoryOpening,
    UpdatedAt = @Now,
    UpdatedBy = N'inventory-valuation-align'
FROM ChartOfAccounts coa
WHERE coa.CompanyId = @CompanyId
  AND coa.IsDeleted = 0
  AND coa.AccountNumber = N'12110';

SELECT AccountNumber, OpeningBalance
FROM ChartOfAccounts
WHERE CompanyId = @CompanyId AND AccountNumber IN (N'12110', N'30000');
