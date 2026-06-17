using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IGlRepairService
{
    Task<GlRepairResult> RepairHistoricalEntriesAsync(CancellationToken cancellationToken = default);

    Task<GlRepairResult> RepairHistoricalEntriesForCompanyAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string? Message, int InvoicesUpdated)> RepairCompany3SalesTaxGlAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string? Message, decimal AmountMoved)> ReallocateSalesTaxOpeningBalanceAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    Task<CutoverReconcileResult> ReconcileToOpeningBalancesAsync(
        int companyId,
        DateTime removeTransactionsOnOrAfter,
        CancellationToken cancellationToken = default);

    Task<TrialBalanceCoaApplyResult> ApplyTrialBalanceCoaOpeningsAsync(
        int companyId,
        string trialBalanceFilePath,
        CancellationToken cancellationToken = default);

    Task<int> BackdateOpeningBalanceJournalsAsync(
        int companyId,
        DateTime entryDate,
        CancellationToken cancellationToken = default);

    Task<int> ResyncSubledgerOpeningBalancesAsync(
        int companyId,
        DateTime entryDate,
        CancellationToken cancellationToken = default);

    Task<TrialBalanceCoaApplyResult> AlignTrialBalanceGlAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    Task<PostCutoverTransactionsResult> PostCutoverTransactionsAsync(
        int companyId,
        DateTime fromDate,
        CancellationToken cancellationToken = default);

    Task<TrialBalanceMismatchFixResult> FixTrialBalanceMismatchesAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    Task<TrialBalanceGapChaseResult> ChaseTrialBalanceGapAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    Task<RecalculateItemStockResult> RecalculateItemStockAsync(
        int companyId,
        CancellationToken cancellationToken = default);
}
