using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IInventoryReportService
{
    Task<StockSummaryReportDto> GetStockSummaryAsync(
        StockSummaryReportRequest request,
        CancellationToken cancellationToken = default);

    Task<LowStockReportDto> GetLowStockReportAsync(
        CancellationToken cancellationToken = default);

    Task<StockMovementReportDto> GetStockMovementReportAsync(
        StockMovementReportRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryReportItemLookupDto>> GetItemLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryReportWarehouseLookupDto>> GetWarehouseLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryReportCategoryLookupDto>> GetCategoryLookupsAsync(
        CancellationToken cancellationToken = default);
}
