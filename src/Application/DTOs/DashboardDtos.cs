namespace PakistanAccountingERP.Application.DTOs;

public record DashboardSummaryDto(
    decimal TodaySales,
    decimal MonthSales,
    decimal OutstandingReceivables,
    decimal OutstandingPayables,
    decimal InventoryValue);

public record MonthlySalesPointDto(string Label, decimal Amount);

public record TopCustomerBalanceDto(int CustomerId, string BuyerName, string BuyerId, decimal Balance);

public record LowStockItemDto(int ItemId, string ItemCode, string ItemName, decimal CurrentStock, decimal MinimumStock, string Unit);

public record RecentInvoiceDto(
    int Id,
    string InvoiceNumber,
    string CustomerName,
    DateTime InvoiceDate,
    decimal NetTotal,
    string Status,
    string StatusBadgeClass);

public record DashboardDataDto(
    DashboardSummaryDto Summary,
    IReadOnlyList<MonthlySalesPointDto> MonthlySales,
    IReadOnlyList<TopCustomerBalanceDto> TopCustomers,
    IReadOnlyList<LowStockItemDto> LowStockItems,
    IReadOnlyList<RecentInvoiceDto> RecentInvoices);
