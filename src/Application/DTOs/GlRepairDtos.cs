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

public record RecalculateItemStockResult(
    bool Success,
    string? Message,
    int ItemsUpdated,
    decimal SumItemStock,
    decimal SumTransactionStock);

