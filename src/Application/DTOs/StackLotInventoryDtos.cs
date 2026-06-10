using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record StackLotAvailabilityDto(
    int ItemId,
    string ItemCode,
    string? StackNo,
    string? LotNo,
    bool Exists,
    decimal PurchasedWeight,
    decimal SoldWeight,
    decimal RemainingWeight,
    decimal PurchasedCartons,
    decimal SoldCartons,
    decimal RemainingCartons);

public record StackLotSaleValidationLine(
    int ItemId,
    string ItemCode,
    string? StackNo,
    string? LotNo,
    decimal Quantity,
    decimal Cartons);

public record LotItemOptionDto(string ItemCode, string LotNo);

public record LotDetailLookupDto(
    string LotNo,
    int ItemId,
    string ItemCode,
    string ItemName,
    string? Description,
    string? HsCode,
    string UnitSymbol,
    decimal SaleRate,
    decimal PurchaseRate,
    string? DefaultStackNo,
    IReadOnlyList<string> StackNos,
    ItemType ItemType);
