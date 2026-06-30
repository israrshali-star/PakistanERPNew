namespace PakistanAccountingERP.Application.DTOs;

public record DashboardSummaryDto(
    decimal TodaySales,
    decimal MonthSales,
    decimal OutstandingReceivables,
    decimal OutstandingPayables,
    decimal InventoryValue,
    decimal CashAndBankBalance);

public record BankCoaClosingBalanceDto(
    string AccountNumber,
    string AccountName,
    decimal ClosingBalance);

public record MonthlySalesPointDto(string Label, decimal Cartons);

public record DailySalesPointDto(string Label, DateTime Date, decimal Cartons);

public record MonthlyProfitLossPointDto(string Label, decimal NetProfit, decimal Revenue, decimal Expenses);

public record TopCustomerBalanceDto(int CustomerId, string BuyerName, string BuyerId, decimal Balance, string BalanceSide);

public record LowStockItemDto(int ItemId, string ItemCode, string ItemName, decimal CurrentStock, decimal MinimumStock, string Unit);

public record RecentInvoiceDto(
    int Id,
    string InvoiceNumber,
    string CustomerName,
    DateTime InvoiceDate,
    decimal NetTotal,
    string Status,
    string StatusBadgeClass);

public record CompanyApClosingBalanceDto(
    int CompanyId,
    string CompanyName,
    decimal ClosingBalance,
    bool IsCurrentCompany);

public record DashboardDataDto(
    DashboardSummaryDto Summary,
    IReadOnlyList<DailySalesPointDto> DailySales,
    IReadOnlyList<MonthlyProfitLossPointDto> MonthlyProfitLoss,
    IReadOnlyList<MonthlySalesPointDto> MonthlySales,
    IReadOnlyList<TopCustomerBalanceDto> TopCustomers,
    IReadOnlyList<LowStockItemDto> LowStockItems,
    IReadOnlyList<RecentInvoiceDto> RecentInvoices,
    IReadOnlyList<CompanyApClosingBalanceDto> ApClosingBalances,
    IReadOnlyList<BankCoaClosingBalanceDto> BankClosingBalances);
