using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Loads financial report datasets from SQL Server stored procedures
/// (SSRS-compatible procs in Infrastructure/Data/Sql/FinancialReports.sql).
/// </summary>
public interface ISqlFinancialReportDataSource
{
    Task EnsureProceduresDeployedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrialBalanceLineDto>> GetTrialBalanceLinesAsync(
        int companyId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProfitAndLossLineDto>> GetProfitAndLossLinesAsync(
        int companyId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BalanceSheetLineDto>> GetBalanceSheetLinesAsync(
        int companyId,
        DateTime asOfDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArAgingLineDto>> GetArAgingLinesAsync(
        int companyId,
        DateTime asOfDate,
        CancellationToken cancellationToken = default);
}
