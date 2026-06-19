using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IInventoryCostingService
{
    Task<InventoryCostingBatch> CreateBatchAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        CancellationToken cancellationToken = default);
}

public sealed class InventoryCostingBatch
{
    private readonly Func<InventoryLineCostRequest, InventoryLineCostResult> _calculate;

    internal InventoryCostingBatch(Func<InventoryLineCostRequest, InventoryLineCostResult> calculate)
    {
        _calculate = calculate;
    }

    public InventoryLineCostResult Calculate(InventoryLineCostRequest request) => _calculate(request);
}

public sealed record InventoryLineCostRequest(
    int ItemId,
    string? StackNo,
    string? LotNo,
    string ItemStackNo,
    string ItemLotNo,
    decimal Quantity,
    CostingMethod CostingMethod,
    decimal FallbackPurchaseRate);

public sealed record InventoryLineCostResult(
    bool Success,
    string? Message,
    decimal UnitCost,
    decimal TotalCost)
{
    public static InventoryLineCostResult Ok(decimal unitCost, decimal totalCost) =>
        new(true, null, unitCost, totalCost);

    public static InventoryLineCostResult Fail(string message) =>
        new(false, message, 0m, 0m);
}
