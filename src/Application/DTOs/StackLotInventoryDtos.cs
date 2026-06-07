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
