/*
  Backfill missing opening-stock inventory transactions for approved OPEN-STOCK vendor bills.
  Mirrors QuickBooksIifImportService opening stock import:
    - TransactionType = 4 (Opening)
    - ReferenceNo = OPEN-STOCK-31052026
  Safe to re-run: idempotent skip on matching ReferenceNo + ItemId + Quantity + Stack/Lot.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @CompanyId INT = 3;
DECLARE @OpeningBillNumber NVARCHAR(50) = N'OPEN-STOCK-31052026';
DECLARE @OpeningRefNo NVARCHAR(50) = N'OPENING-31MAY2026';
DECLARE @OpeningDate DATE = '2026-05-31';
DECLARE @WarehouseId INT;
DECLARE @Now DATETIME2 = SYSUTCDATETIME();
DECLARE @CreatedBy NVARCHAR(256) = N'backfill-script';

SELECT TOP 1 @WarehouseId = Id
FROM Warehouses
WHERE CompanyId = @CompanyId
  AND IsDeleted = 0
  AND IsActive = 1
ORDER BY Code;

IF @WarehouseId IS NULL
BEGIN
    RAISERROR('No active warehouse found for CompanyId %d.', 16, 1, @CompanyId);
    RETURN;
END;

BEGIN TRANSACTION;

IF OBJECT_ID('tempdb..#BackfillLines') IS NOT NULL DROP TABLE #BackfillLines;
CREATE TABLE #BackfillLines
(
    VendorBillLineId INT NOT NULL PRIMARY KEY,
    CompanyId INT NOT NULL,
    ItemId INT NOT NULL,
    ItemCode NVARCHAR(50) NOT NULL,
    BillNumber NVARCHAR(50) NOT NULL,
    BillDate DATE NOT NULL,
    Quantity DECIMAL(18, 2) NOT NULL,
    Cartons DECIMAL(18, 2) NOT NULL,
    UnitCost DECIMAL(18, 2) NOT NULL,
    StackNo NVARCHAR(50) NULL,
    LotNo NVARCHAR(50) NULL
);

INSERT INTO #BackfillLines
(
    VendorBillLineId,
    CompanyId,
    ItemId,
    ItemCode,
    BillNumber,
    BillDate,
    Quantity,
    Cartons,
    UnitCost,
    StackNo,
    LotNo
)
SELECT
    l.Id,
    b.CompanyId,
    l.ItemId,
    i.ItemCode,
    b.BillNumber,
    CAST(b.BillDate AS DATE),
    ROUND(l.Quantity, 2) AS Quantity,
    ROUND(l.Cartons, 2) AS Cartons,
    ROUND(l.Rate, 2) AS UnitCost,
    NULLIF(LTRIM(RTRIM(l.StackNo)), N'') AS StackNo,
    NULLIF(LTRIM(RTRIM(l.LotNo)), N'') AS LotNo
FROM VendorBillLines l
INNER JOIN VendorBills b ON b.Id = l.VendorBillId
INNER JOIN Items i ON i.Id = l.ItemId
WHERE b.CompanyId = @CompanyId
  AND b.Status = 2 -- Approved
  AND (b.BillNumber = @OpeningBillNumber OR b.RefNo = @OpeningRefNo)
  AND l.ItemId IS NOT NULL
  AND ROUND(l.Quantity, 2) > 0
  AND NOT EXISTS
  (
      SELECT 1
      FROM InventoryTransactions it
      WHERE it.CompanyId = @CompanyId
        AND it.ReferenceNo = @OpeningBillNumber
        AND it.ItemId = l.ItemId
        AND it.TransactionType = 4 -- Opening
        AND it.Quantity = ROUND(l.Quantity, 2)
        AND ISNULL(it.StackNo, N'') = ISNULL(NULLIF(LTRIM(RTRIM(l.StackNo)), N''), N'')
        AND ISNULL(it.LotNo, N'') = ISNULL(NULLIF(LTRIM(RTRIM(l.LotNo)), N''), N'')
        AND it.IsDeleted = 0
  );

DECLARE @CandidateCount INT = (SELECT COUNT(*) FROM #BackfillLines);
DECLARE @InsertedCount INT = 0;

INSERT INTO InventoryTransactions
(
    CompanyId,
    ItemId,
    WarehouseId,
    TransactionType,
    StackNo,
    LotNo,
    Quantity,
    UnitCost,
    TotalCost,
    TransactionDate,
    ReferenceNo,
    Notes,
    CreatedAt,
    CreatedBy,
    IsDeleted
)
SELECT
    bl.CompanyId,
    bl.ItemId,
    @WarehouseId,
    4 AS TransactionType,
    bl.StackNo,
    bl.LotNo,
    bl.Quantity,
    bl.UnitCost,
    ROUND(bl.Quantity * bl.UnitCost, 2) AS TotalCost,
    bl.BillDate,
    bl.BillNumber,
    N'Opening stock ' + bl.BillNumber,
    @Now,
    @CreatedBy,
    0
FROM #BackfillLines bl;

SET @InsertedCount = @@ROWCOUNT;

PRINT '=== Opening stock backfill summary ===';
PRINT 'CompanyId: ' + CAST(@CompanyId AS NVARCHAR(20));
PRINT 'WarehouseId: ' + CAST(@WarehouseId AS NVARCHAR(20));
PRINT 'Candidate lines: ' + CAST(@CandidateCount AS NVARCHAR(20));
PRINT 'Transactions inserted: ' + CAST(@InsertedCount AS NVARCHAR(20));

SELECT
    COUNT(*) AS OpeningTransactionsAfter,
    SUM(Quantity) AS TotalOpeningWeight,
    SUM(Cartons) AS TotalOpeningCartons
FROM #BackfillLines;

SELECT TOP 10
    ItemCode,
    Quantity,
    Cartons,
    StackNo,
    LotNo
FROM #BackfillLines
ORDER BY Quantity DESC;

COMMIT TRANSACTION;

PRINT 'Opening stock backfill completed successfully.';
