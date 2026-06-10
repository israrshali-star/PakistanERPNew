/*
  Backfill missing inventory transactions for posted sales invoices / credit notes.
  Mirrors SalesInvoiceService.PostAsync stock movement logic:
    - SalesInvoice  -> StockOut (TransactionType = 2), reduces CurrentStock
    - CreditNote    -> StockIn  (TransactionType = 1), increases CurrentStock
  Skips service items, cartage (ITEM-0002), and lines already booked under INV-* references.
  Safe to re-run: idempotent skip on matching ReferenceNo + ItemId + Quantity + Stack/Lot.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @CompanyId INT = 3;
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
    SalesInvoiceLineId INT NOT NULL PRIMARY KEY,
    CompanyId INT NOT NULL,
    ItemId INT NOT NULL,
    ItemCode NVARCHAR(50) NOT NULL,
    InvoiceNumber NVARCHAR(50) NOT NULL,
    InvoiceDate DATE NOT NULL,
    InvoiceType INT NOT NULL,
    TransactionType INT NOT NULL,
    Quantity DECIMAL(18, 2) NOT NULL,
    UnitCost DECIMAL(18, 2) NOT NULL,
    StackNo NVARCHAR(50) NULL,
    LotNo NVARCHAR(50) NULL
);

INSERT INTO #BackfillLines
(
    SalesInvoiceLineId,
    CompanyId,
    ItemId,
    ItemCode,
    InvoiceNumber,
    InvoiceDate,
    InvoiceType,
    TransactionType,
    Quantity,
    UnitCost,
    StackNo,
    LotNo
)
SELECT
    l.Id,
    si.CompanyId,
    l.ItemId,
    i.ItemCode,
    si.InvoiceNumber,
    CAST(si.InvoiceDate AS DATE),
    si.InvoiceType,
    CASE WHEN si.InvoiceType = 3 THEN 1 ELSE 2 END AS TransactionType,
    ROUND(l.Quantity, 2) AS Quantity,
    ROUND(i.PurchaseRate, 2) AS UnitCost,
    NULLIF(LTRIM(RTRIM(l.StackNo)), N'') AS StackNo,
    NULLIF(LTRIM(RTRIM(l.LotNo)), N'') AS LotNo
FROM SalesInvoiceLines l
INNER JOIN SalesInvoices si ON si.Id = l.SalesInvoiceId
INNER JOIN Items i ON i.Id = l.ItemId
WHERE si.CompanyId = @CompanyId
  AND si.Status = 2 -- Posted
  AND si.InvoiceType IN (1, 3) -- SalesInvoice, CreditNote (not DebitNote)
  AND i.CompanyId = @CompanyId
  AND i.ItemType = 1 -- Goods
  AND i.ItemCode <> N'ITEM-0002' -- cartage
  AND ROUND(l.Quantity, 2) > 0
  AND NOT EXISTS
  (
      SELECT 1
      FROM InventoryTransactions it
      WHERE it.CompanyId = @CompanyId
        AND it.ReferenceNo = si.InvoiceNumber
        AND it.ItemId = l.ItemId
        AND it.TransactionType = CASE WHEN si.InvoiceType = 3 THEN 1 ELSE 2 END
        AND it.Quantity = ROUND(l.Quantity, 2)
        AND ISNULL(it.StackNo, N'') = ISNULL(NULLIF(LTRIM(RTRIM(l.StackNo)), N''), N'')
        AND ISNULL(it.LotNo, N'') = ISNULL(NULLIF(LTRIM(RTRIM(l.LotNo)), N''), N'')
        AND it.IsDeleted = 0
  );

IF OBJECT_ID('tempdb..#StockBefore') IS NOT NULL DROP TABLE #StockBefore;
CREATE TABLE #StockBefore
(
    ItemId INT NOT NULL PRIMARY KEY,
    ItemCode NVARCHAR(50) NOT NULL,
    CurrentStock DECIMAL(18, 2) NOT NULL
);

INSERT INTO #StockBefore (ItemId, ItemCode, CurrentStock)
SELECT DISTINCT i.Id, i.ItemCode, i.CurrentStock
FROM Items i
WHERE i.Id IN (SELECT DISTINCT ItemId FROM #BackfillLines);

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
    bl.TransactionType,
    bl.StackNo,
    bl.LotNo,
    bl.Quantity,
    bl.UnitCost,
    ROUND(bl.Quantity * bl.UnitCost, 2) AS TotalCost,
    bl.InvoiceDate,
    bl.InvoiceNumber,
    N'Sales invoice ' + bl.InvoiceNumber,
    @Now,
    @CreatedBy,
    0
FROM #BackfillLines bl;

SET @InsertedCount = @@ROWCOUNT;

DECLARE @CandidateCount INT = (SELECT COUNT(*) FROM #BackfillLines);

UPDATE i
SET
    CurrentStock = ROUND(
        i.CurrentStock - adj.NetStockDelta,
        2),
    UpdatedAt = @Now,
    UpdatedBy = @CreatedBy
FROM Items i
INNER JOIN
(
    SELECT
        ItemId,
        SUM(CASE WHEN InvoiceType = 1 THEN Quantity ELSE -Quantity END) AS NetStockDelta
    FROM #BackfillLines
    GROUP BY ItemId
) adj ON adj.ItemId = i.Id;

PRINT '=== Backfill summary ===';
PRINT 'CompanyId: ' + CAST(@CompanyId AS NVARCHAR(20));
PRINT 'WarehouseId: ' + CAST(@WarehouseId AS NVARCHAR(20));
PRINT 'Candidate lines: ' + CAST(@CandidateCount AS NVARCHAR(20));
PRINT 'Transactions inserted: ' + CAST(@InsertedCount AS NVARCHAR(20));

SELECT
    TransactionType,
    CASE TransactionType WHEN 1 THEN N'StockIn' WHEN 2 THEN N'StockOut' ELSE N'Other' END AS TypeName,
    COUNT(*) AS LineCount,
    SUM(Quantity) AS TotalQuantity
FROM #BackfillLines
GROUP BY TransactionType
ORDER BY TransactionType;

SELECT
    COUNT(*) AS StockOutTransactionsAfter,
    SUM(CASE WHEN TransactionType = 2 THEN Quantity ELSE 0 END) AS TotalStockOutQty
FROM InventoryTransactions
WHERE CompanyId = @CompanyId
  AND IsDeleted = 0
  AND TransactionType = 2;

SELECT TOP 10
    b.ItemCode,
    b.CurrentStock AS StockBefore,
    i.CurrentStock AS StockAfter,
    ROUND(b.CurrentStock - i.CurrentStock, 2) AS StockReduced,
    adj.NetSold
FROM #StockBefore b
INNER JOIN Items i ON i.Id = b.ItemId
INNER JOIN
(
    SELECT
        ItemId,
        SUM(CASE WHEN InvoiceType = 1 THEN Quantity ELSE -Quantity END) AS NetSold
    FROM #BackfillLines
    GROUP BY ItemId
) adj ON adj.ItemId = b.ItemId
ORDER BY adj.NetSold DESC;

COMMIT TRANSACTION;

PRINT 'Backfill completed successfully.';
