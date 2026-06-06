using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record ItemDto(
    int Id,
    ItemType ItemType,
    string ItemCode,
    string ItemName,
    string StackNo,
    string LotNo,
    string? Description,
    string? HSCode,
    string? Barcode,
    int UnitOfMeasureId,
    string UnitSymbol,
    int? ItemCategoryId,
    string? CategoryName,
    decimal PurchaseRate,
    decimal SaleRate,
    decimal MinimumStock,
    decimal ReorderLevel,
    decimal CurrentStock,
    CostingMethod CostingMethod,
    bool IsActive,
    bool HasTransactions);

public record ItemListItemDto(
    int Id,
    string ItemCode,
    string ItemName,
    string ItemType,
    string? CategoryName,
    string UnitSymbol,
    decimal SaleRate,
    decimal CurrentStock,
    bool IsActive);

public class ItemSaveRequest
{
    public int? Id { get; set; }
    public ItemType ItemType { get; set; } = ItemType.Goods;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string StackNo { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? HSCode { get; set; }
    public string? Barcode { get; set; }
    public int UnitOfMeasureId { get; set; }
    public int? ItemCategoryId { get; set; }
    public decimal PurchaseRate { get; set; }
    public decimal SaleRate { get; set; }
    public decimal MinimumStock { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal? OpeningStock { get; set; }
    public CostingMethod CostingMethod { get; set; } = CostingMethod.FIFO;
    public bool IsActive { get; set; } = true;
}

public record ItemSaveResult(bool Success, string? Message, ItemDto? Item);

public record NextItemCodeDto(string ItemCode);

public record ItemCategoryLookupDto(int Id, string Name);
