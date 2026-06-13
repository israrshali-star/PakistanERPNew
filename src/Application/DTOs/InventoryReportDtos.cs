namespace PakistanAccountingERP.Application.DTOs;

public record StockSummaryLineDto(
    int ItemId,
    string ItemCode,
    string ItemName,
    string? CategoryName,
    string UnitSymbol,
    decimal CurrentStock,
    decimal CurrentCartons,
    decimal MinimumStock,
    decimal ReorderLevel,
    decimal PurchaseRate,
    decimal StockValue);

public record StockSummaryReportDto(
    DateTime GeneratedAt,
    DateTime AsOfDate,
    int ItemCount,
    decimal TotalStock,
    decimal TotalCartons,
    decimal TotalStockValue,
    IReadOnlyList<StockSummaryLineDto> Lines);

public record StackWiseStockLineDto(
    int ItemId,
    string ItemCode,
    string ItemName,
    string? CategoryName,
    string? LotNo,
    string? StackNo,
    decimal Cartons,
    decimal Quantity,
    decimal PurchaseRate,
    decimal StockValue);

public record StackWiseStockReportDto(
    DateTime GeneratedAt,
    DateTime AsOfDate,
    int LineCount,
    decimal TotalStock,
    decimal TotalCartons,
    decimal TotalStockValue,
    IReadOnlyList<StackWiseStockLineDto> Lines);

public record LowStockLineDto(
    int ItemId,
    string ItemCode,
    string ItemName,
    string? CategoryName,
    string UnitSymbol,
    decimal CurrentStock,
    decimal MinimumStock,
    decimal ReorderLevel,
    decimal Shortfall);

public record LowStockReportDto(
    DateTime GeneratedAt,
    int ItemCount,
    IReadOnlyList<LowStockLineDto> Lines);

public record StockMovementLineDto(
    DateTime TransactionDate,
    string? ReferenceNo,
    string TransactionType,
    string ItemCode,
    string ItemName,
    string WarehouseName,
    decimal QtyIn,
    decimal QtyOut,
    decimal CartonsIn,
    decimal CartonsOut,
    decimal AdjustmentQty,
    decimal UnitCost,
    decimal TotalCost,
    string? StackNo,
    string? LotNo,
    string? Notes);

public record StockMovementReportDto(
    DateTime FromDate,
    DateTime ToDate,
    int? ItemId,
    string? ItemLabel,
    int? WarehouseId,
    string? WarehouseLabel,
    int TransactionCount,
    decimal TotalQtyIn,
    decimal TotalQtyOut,
    decimal TotalCartonsIn,
    decimal TotalCartonsOut,
    IReadOnlyList<StockMovementLineDto> Lines);

public record InventoryReportItemLookupDto(int Id, string ItemCode, string ItemName);

public record InventoryReportWarehouseLookupDto(int Id, string Code, string Name);

public record InventoryReportCategoryLookupDto(int Id, string Name);

public class StockSummaryReportRequest
{
    public int? CategoryId { get; set; }
    public bool ActiveOnly { get; set; } = true;
    public bool HideZeroQoh { get; set; }
    public DateTime? AsOfDate { get; set; }
}

public class StockMovementReportRequest
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int? ItemId { get; set; }
    public int? WarehouseId { get; set; }
}
