using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IFinancialReportService
{
    Task<TrialBalanceReportDto> GetTrialBalanceAsync(
        FinancialReportDateRangeRequest request,
        CancellationToken cancellationToken = default);

    Task<ProfitAndLossReportDto> GetProfitAndLossAsync(
        FinancialReportDateRangeRequest request,
        CancellationToken cancellationToken = default);

    Task<BalanceSheetReportDto> GetBalanceSheetAsync(
        BalanceSheetReportRequest request,
        CancellationToken cancellationToken = default);
}
