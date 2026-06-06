using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record InventoryTransactionDto(
    int Id,
    string? ReferenceNo,
    int ItemId,
    string ItemCode,
    string ItemName,
    int WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    InventoryTransactionType TransactionType,
    string? StackNo,
    string? LotNo,
    decimal Quantity,
    decimal UnitCost,
    decimal TotalCost,
    DateTime TransactionDate,
    string? Notes);

public record InventoryTransactionListItemDto(
    int Id,
    string? ReferenceNo,
    DateTime TransactionDate,
    string TransactionType,
    string ItemCode,
    string ItemName,
    string WarehouseName,
    decimal Quantity,
    decimal TotalCost);

public class InventoryTransactionSaveRequest
{
    public int ItemId { get; set; }
    public int WarehouseId { get; set; }
    public InventoryTransactionType TransactionType { get; set; } = InventoryTransactionType.StockIn;
    public string? StackNo { get; set; }
    public string? LotNo { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Notes { get; set; }
}

public record InventoryTransactionSaveResult(
    bool Success,
    string? Message,
    InventoryTransactionDto? Transaction);

public record NextStockReferenceDto(string ReferenceNo);

public record InventoryItemLookupDto(
    int Id,
    string ItemCode,
    string ItemName,
    decimal CurrentStock,
    string UnitSymbol);

public record InventoryWarehouseLookupDto(int Id, string Code, string Name);
