SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @CompanyId INT = 2;
DECLARE @Now DATETIME2 = SYSUTCDATETIME();

IF NOT EXISTS (
    SELECT 1 FROM Vendors
    WHERE CompanyId = @CompanyId AND VendorName = N'Yarn Merchants' AND IsDeleted = 0)
BEGIN
    INSERT INTO Vendors (
        CompanyId, VendorCode, VendorName, OpeningBalance, DefaultSalesTaxRate,
        IsActive, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted)
    VALUES (
        @CompanyId, N'VEND-0001', N'Yarn Merchants', 0, 18,
        1, @Now, N'al-aziz-import', @Now, N'al-aziz-import', 0);
END

SELECT VendorCode, VendorName, OpeningBalance
FROM Vendors
WHERE CompanyId = @CompanyId AND IsDeleted = 0;
