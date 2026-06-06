namespace PakistanAccountingERP.Application.DTOs;

public record TrialBalanceLineDto(
    int AccountId,
    string AccountNumber,
    string AccountName,
    string? TypeName,
    decimal OpeningBalance,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal ClosingBalance,
    decimal ClosingDebit,
    decimal ClosingCredit);

public record TrialBalanceReportDto(
    DateTime FromDate,
    DateTime ToDate,
    int AccountCount,
    decimal TotalClosingDebit,
    decimal TotalClosingCredit,
    IReadOnlyList<TrialBalanceLineDto> Lines);

public record ProfitAndLossLineDto(
    int AccountId,
    string AccountNumber,
    string AccountName,
    string Section,
    decimal Amount);

public record ProfitAndLossReportDto(
    DateTime FromDate,
    DateTime ToDate,
    decimal TotalRevenue,
    decimal TotalCogs,
    decimal TotalExpenses,
    decimal GrossProfit,
    decimal NetProfit,
    IReadOnlyList<ProfitAndLossLineDto> Lines);

public record BalanceSheetLineDto(
    int AccountId,
    string AccountNumber,
    string AccountName,
    string Section,
    decimal Amount);

public record BalanceSheetReportDto(
    DateTime AsOfDate,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    decimal NetIncomeYtd,
    decimal TotalLiabilitiesAndEquity,
    IReadOnlyList<BalanceSheetLineDto> Lines);

public class FinancialReportDateRangeRequest
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
}

public class BalanceSheetReportRequest
{
    public DateTime AsOfDate { get; set; }
}
