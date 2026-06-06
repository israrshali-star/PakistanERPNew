using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Company-scoped dashboard metrics and lists for the home page.
/// </summary>
public interface IDashboardService
{
    /// <summary>Returns KPI summary cards for the active company.</summary>
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns monthly posted sales totals for the last 12 months.</summary>
    Task<IReadOnlyList<MonthlySalesPointDto>> GetMonthlySalesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns top customers by outstanding balance (opening + posted invoices minus receipts).</summary>
    Task<IReadOnlyList<TopCustomerBalanceDto>> GetTopCustomersByBalanceAsync(
        int count = 5,
        CancellationToken cancellationToken = default);

    /// <summary>Returns items where current stock is below minimum stock.</summary>
    Task<IReadOnlyList<LowStockItemDto>> GetLowStockItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent sales invoices for the active company.</summary>
    Task<IReadOnlyList<RecentInvoiceDto>> GetRecentInvoicesAsync(
        int count = 5,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all dashboard sections in one payload.</summary>
    Task<DashboardDataDto> GetDashboardDataAsync(CancellationToken cancellationToken = default);
}
