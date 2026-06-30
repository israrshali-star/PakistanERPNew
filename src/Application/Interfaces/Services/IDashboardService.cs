using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Company-scoped dashboard metrics and lists for the home page.
/// </summary>
public interface IDashboardService
{
    /// <summary>Returns KPI summary cards for the active company.</summary>
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns daily posted sales cartons for the last 30 days.</summary>
    Task<IReadOnlyList<DailySalesPointDto>> GetDailySalesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns monthly net profit or loss from GL for the last 12 months.</summary>
    Task<IReadOnlyList<MonthlyProfitLossPointDto>> GetMonthlyProfitLossAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns monthly posted sales cartons for the last 12 months.</summary>
    Task<IReadOnlyList<MonthlySalesPointDto>> GetMonthlySalesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns top debit and credit customer balances (opening + posted activity).</summary>
    Task<IReadOnlyList<TopCustomerBalanceDto>> GetTopCustomersByBalanceAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Returns items where current stock is below minimum stock.</summary>
    Task<IReadOnlyList<LowStockItemDto>> GetLowStockItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent sales invoices for the active company.</summary>
    Task<IReadOnlyList<RecentInvoiceDto>> GetRecentInvoicesAsync(
        int count = 5,
        CancellationToken cancellationToken = default);

    /// <summary>Returns GL AP (20000) closing balances for purchase-tax companies the user can access.</summary>
    Task<IReadOnlyList<CompanyApClosingBalanceDto>> GetApClosingBalancesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Returns GL closing balances for cash and bank COA leaf accounts.</summary>
    Task<IReadOnlyList<BankCoaClosingBalanceDto>> GetBankClosingBalancesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Returns all dashboard sections in one payload.</summary>
    Task<DashboardDataDto> GetDashboardDataAsync(CancellationToken cancellationToken = default);
}
