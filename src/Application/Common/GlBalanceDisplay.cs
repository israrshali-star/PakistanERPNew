using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;

namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// Normalizes stored GL net balances for UI and reports.
/// QuickBooks import stores AR debit balances as negative; liabilities/equity use credit-normal storage.
/// </summary>
public static class GlBalanceDisplay
{
    private const int LiabilityTypeId = 2;

    public static decimal NormalizeNetForDisplay(decimal netBalance, int? typeId, string? accountNumber)
    {
        if (UsesInvertedStorageDisplay(typeId, accountNumber))
        {
            return -netBalance;
        }

        return netBalance;
    }

    public static bool UsesInvertedStorageDisplay(int? typeId, string? accountNumber) =>
        string.Equals(accountNumber, AccountsReceivable, StringComparison.OrdinalIgnoreCase)
        || (typeId == LiabilityTypeId
            && !string.Equals(accountNumber, AccountsPayable, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// For companies 2/4/5/6/7, sales tax uses normal debit−credit accumulation on the flipped
    /// opening so invoice credits reduce the remaining credit balance.
    /// </summary>
    public static bool UsesInvertedLineAccumulation(
        int? typeId,
        string? accountNumber,
        int? companyId = null) =>
        string.Equals(accountNumber, AccountsPayable, StringComparison.OrdinalIgnoreCase)
        || (UsesInvertedStorageDisplay(typeId, accountNumber)
            && !UsesSalesTaxInvoiceCreditReduction(companyId, accountNumber));

    public static bool UsesSalesTaxInvoiceCreditReduction(int? companyId, string? accountNumber) =>
        companyId.HasValue
        && TradeInvoiceLayout.InvoiceCreditsReduceSalesTaxBalance(companyId.Value)
        && GlOpeningBalanceNormalizer.IsSalesTaxLiabilityAccount(accountNumber);
}
