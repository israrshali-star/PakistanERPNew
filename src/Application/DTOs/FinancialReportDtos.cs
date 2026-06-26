namespace PakistanAccountingERP.Application.DTOs;

public enum FinancialReportRowKind
{
    SectionHeader = 1,
    GroupHeader = 2,
    Account = 3,
    Subtotal = 4,
    Total = 5
}

public record FinancialReportRowDto(
    string Label,
    int IndentLevel,
    FinancialReportRowKind Kind,
    decimal? Debit,
    decimal? Credit,
    decimal? Amount);

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
    bool IsBalanced,
    decimal Difference,
    IReadOnlyList<TrialBalanceLineDto> Lines,
    IReadOnlyList<FinancialReportRowDto> Rows);

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
    IReadOnlyList<ProfitAndLossLineDto> Lines,
    IReadOnlyList<FinancialReportRowDto> Rows);

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
    bool IsBalanced,
    decimal Difference,
    IReadOnlyList<BalanceSheetLineDto> Lines,
    IReadOnlyList<FinancialReportRowDto> Rows);

public class FinancialReportDateRangeRequest
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
}

public class BalanceSheetReportRequest
{
    public DateTime AsOfDate { get; set; }
}

public record ArAgingLineDto(
    int CustomerId,
    string CustomerCode,
    string CustomerName,
    decimal OpeningBalance,
    decimal Current,
    decimal Days31To60,
    decimal Days61To90,
    decimal Over90,
    decimal Total);

public record ArAgingSummaryReportDto(
    DateTime AsOfDate,
    int CustomerCount,
    decimal TotalOpeningBalance,
    decimal TotalCurrent,
    decimal TotalDays31To60,
    decimal TotalDays61To90,
    decimal TotalOver90,
    decimal GrandTotal,
    IReadOnlyList<ArAgingLineDto> Lines);

public class ArAgingReportRequest
{
    public DateTime AsOfDate { get; set; }
}
