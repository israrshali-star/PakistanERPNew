-- Sync opening stock line costs from item purchase rates (company 2).
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @CompanyId INT = 2;
DECLARE @BillNumber NVARCHAR(50) = N'OPEN-STOCK-31052026';
DECLARE @Now DATETIME2 = SYSUTCDATETIME();

UPDATE vbl
SET Rate = i.PurchaseRate,
    Amount = ROUND(vbl.Quantity * i.PurchaseRate, 2)
FROM VendorBillLines vbl
INNER JOIN VendorBills vb ON vb.Id = vbl.VendorBillId
INNER JOIN Items i ON i.Id = vbl.ItemId
WHERE vb.CompanyId = @CompanyId
  AND vb.BillNumber = @BillNumber
  AND vb.IsDeleted = 0
  AND i.PurchaseRate > 0;

UPDATE vb
SET NetAmount = ISNULL((
        SELECT SUM(vbl.Amount)
        FROM VendorBillLines vbl
        WHERE vbl.VendorBillId = vb.Id), 0),
    UpdatedAt = @Now,
    UpdatedBy = N'al-aziz-valuation'
FROM VendorBills vb
WHERE vb.CompanyId = @CompanyId
  AND vb.BillNumber = @BillNumber
  AND vb.IsDeleted = 0;

UPDATE it
SET UnitCost = i.PurchaseRate,
    TotalCost = ROUND(it.Quantity * i.PurchaseRate, 2),
    UpdatedAt = @Now,
    UpdatedBy = N'al-aziz-valuation'
FROM InventoryTransactions it
INNER JOIN Items i ON i.Id = it.ItemId
WHERE it.CompanyId = @CompanyId
  AND it.ReferenceNo = @BillNumber
  AND it.IsDeleted = 0
  AND i.PurchaseRate > 0;

SELECT vb.BillNumber, vb.Status, vb.NetAmount
FROM VendorBills vb
WHERE vb.CompanyId = @CompanyId AND vb.BillNumber = @BillNumber;

SELECT i.ItemCode, vbl.Quantity, vbl.Rate, vbl.Amount
FROM VendorBillLines vbl
INNER JOIN VendorBills vb ON vb.Id = vbl.VendorBillId
INNER JOIN Items i ON i.Id = vbl.ItemId
WHERE vb.CompanyId = @CompanyId AND vb.BillNumber = @BillNumber
ORDER BY i.ItemCode;

SELECT SUM(ROUND(CurrentStock * PurchaseRate, 2)) AS ItemInventoryValue
FROM Items
WHERE CompanyId = @CompanyId AND IsDeleted = 0 AND CurrentStock > 0 AND ItemCode LIKE 'W%';
