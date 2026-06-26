namespace PakistanAccountingERP.Application.DTOs;



public record GlRepairResult(

    bool Success,

    string? Message,

    int LegacyCoaLinesRemapped,

    int CartageJournalLinesAdded,

    int CartageRevenueCreditsAdjusted,

    int SalesInvoiceCogsLinesAdded,

    int DuplicateJournalsSoftDeleted,

    int OrphanJournalsSoftDeleted,

    int ParentArLinesConsolidated,

    int CustomerOpeningBalanceJournalsResynced,

    int DuplicateReceiptJournalsSoftDeleted,

    int DuplicateBankTransactionJournalsSoftDeleted,

    int DeletedJournalLinesPurged,

    decimal AccountsReceivableBalance);

public record CutoverReconcileResult(
    bool Success,
    string? Message,
    int TransactionalJournalsSoftDeleted,
    int SalesInvoicesReverted,
    int CustomerReceiptsRemoved,
    int BankTransactionsRemoved,
    int VendorBillsReverted,
    decimal AccountsReceivableBalance,
    decimal AccountsPayableBalance,
    decimal SumCustomerOpeningBalance,
    decimal SumVendorOpeningBalance);

public record TrialBalanceCoaApplyResult(
    bool Success,
    string? Message,
    int AccountsUpdated,
    int AccountsSkipped,
    int BanksSynced,
    decimal AccountsReceivableBalance,
    decimal AccountsPayableBalance);

public record PostCutoverTransactionsResult(
    bool Success,
    string? Message,
    int JournalsRestored,
    int SalesInvoicesPosted,
    int VendorBillsApproved,
    int CustomerReceiptsRestored,
    int BankTransactionsRestored,
    int SkippedDuplicates,
    decimal AccountsReceivableBalance,
    decimal AccountsPayableBalance,
    decimal TrialBalanceDebits,
    decimal TrialBalanceCredits);

public record TrialBalanceMismatchFixResult(
    bool Success,
    string? Message,
    int CustomerReceiptJournalsFixed,
    int DuplicateVendorBillsReversed,
    bool KeptAsideOpeningSet,
    decimal CashBalance,
    decimal AccountsReceivableBalance,
    decimal InventoryBalance,
    decimal AccountsPayableBalance,
    decimal KeptAsideBalance,
    decimal TrialBalanceDebits,
    decimal TrialBalanceCredits);

public record TrialBalanceGapChaseResult(
    bool Success,
    string? Message,
    int SalesTaxPaymentsReclassified,
    int BankTransactionsReposted,
    decimal AccountsReceivableBalance,
    decimal AccountsPayableBalance,
    decimal SalesTaxPayableBalance,
    decimal TrialBalanceDebits,
    decimal TrialBalanceCredits,
    decimal QuickBooksTotalDebits,
    decimal RemainingGapDebits);

public record OpeningBalanceEquityReplugResult(
    bool Success,
    string? Message,
    decimal PreviousOpeningBalanceEquity,
    decimal NewOpeningBalanceEquity,
    decimal TrialBalanceDebits,
    decimal TrialBalanceCredits);

public record RecalculateItemStockResult(
    bool Success,
    string? Message,
    int ItemsUpdated,
    decimal SumItemStock,
    decimal SumTransactionStock);

public record DeletedSalesInvoiceInventoryRepairResult(
    bool Success,
    string? Message,
    int InventoryTransactionsRemoved,
    int JournalEntriesRemoved,
    int ItemsUpdated);

public record SalesInvoiceCogsRepairResult(
    bool Success,
    string? Message,
    int OpeningTransactionsFixed,
    int InvoicesAdjusted,
    decimal TotalCogsAdjusted);

public record InventoryAssetAlignResult(
    bool Success,
    string? Message,
    decimal QuickBooksClosingBalance,
    decimal OldOpeningBalance,
    decimal NewOpeningBalance,
    decimal JournalNet,
    decimal ErpClosingBalance,
    decimal ItemValuation,
    decimal DifferenceVsQuickBooks);

public record QuickBooksControlBalanceAlignResult(
    bool Success,
    string? Message,
    decimal AccountsReceivableBalance,
    decimal AccountsPayableBalance,
    decimal InventoryBalance,
    decimal? FurtherTaxBalance,
    decimal? SalesTax18Balance,
    decimal OpeningBalanceEquity,
    decimal TrialBalanceDebits,
    decimal TrialBalanceCredits);

public record InventoryAssetRepairResult(
    bool Success,
    string? Message,
    int VendorBillsUpdated,
    int SalesInvoicesUpdated,
    decimal QuickBooksClosingBalance,
    decimal ErpClosingBalance,
    decimal DifferenceVsQuickBooks);

public record SalesTaxSubAccountRepairResult(
    bool Success,
    string? Message,
    decimal FurtherTaxOpening,
    decimal SalesTax18Opening,
    int BankPaymentsReposted,
    decimal ParentSalesTaxBalance,
    decimal FurtherTaxBalance,
    decimal SalesTax18Balance,
    decimal OpeningBalanceEquity,
    decimal TrialBalanceDebits,
    decimal TrialBalanceCredits);

public record VendorBillApMismatchDto(
    string RefNo,
    string? ErpBillNumber,
    string VendorName,
    decimal QuickBooksNetAmount,
    decimal ErpNetAmount,
    decimal Difference);

public record VendorBillApRepairResult(
    bool Success,
    string? Message,
    int BillsChecked,
    int BillsUpdated,
    int BillsMissingInErp,
    int BillsMissingInQuickBooks,
    decimal QuickBooksClosingBalance,
    decimal ErpAccountsPayableBalance,
    decimal DifferenceVsQuickBooks,
    IReadOnlyList<VendorBillApMismatchDto> Mismatches);
