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

    Task<(bool Success, string? Message, int InvoicesFixed, decimal AccountsReceivableBalance)> RepairAccountsReceivableGlAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    Task<OpeningBalanceEquityReplugResult> ReplugOpeningBalanceEquityAsync(
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

    Task<DeletedSalesInvoiceInventoryRepairResult> RepairDeletedSalesInvoiceInventoryAsync(
        int companyId,
        string? invoiceNumber = null,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceCogsRepairResult> RepairUnderstatedSalesInvoiceCogsAsync(
        int companyId,
        string? invoiceNumber = null,
        CancellationToken cancellationToken = default);

    Task<InventoryAssetAlignResult> AlignInventoryAssetToQuickBooksAsync(
        int companyId,
        decimal quickBooksClosingBalance,
        CancellationToken cancellationToken = default);

    Task<InventoryAssetAlignResult> AlignInventoryAssetToStockSummaryAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    Task<QuickBooksControlBalanceAlignResult> AlignControlAccountsToQuickBooksAsync(
        int companyId,
        decimal accountsReceivableBalance,
        decimal accountsPayableBalance,
        decimal inventoryBalance,
        decimal? furtherTaxBalance = null,
        decimal? salesTax18Balance = null,
        CancellationToken cancellationToken = default);

    Task<QuickBooksControlBalanceAlignResult> AlignSalesTaxFromQuickBooksAsync(
        int companyId,
        string salesTaxPayableFilePath,
        CancellationToken cancellationToken = default);

    Task<InventoryAssetRepairResult> RepairInventoryAssetFromQuickBooksAsync(
        int companyId,
        string quickBooksLedgerFilePath,
        CancellationToken cancellationToken = default);

    Task<SalesTaxSubAccountRepairResult> RepairSalesTaxSubAccountTrialBalanceAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    Task<VendorBillApRepairResult> RepairVendorBillsFromQuickBooksApAsync(
        int companyId,
        string accountsPayableFilePath,
        bool applyFixes = true,
        CancellationToken cancellationToken = default);
}
