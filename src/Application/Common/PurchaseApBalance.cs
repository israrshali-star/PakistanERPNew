using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;

namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// QuickBooks AP presentation for purchase-tax companies (2, 4, 5, 6, 7).
/// QB account ledgers show negative balances when the company owes vendors.
/// ERP stored AP net is credit-normal: opening + credits − debits (positive = owed).
/// Signed display = −storedNet so negative means owed (matches QB).
/// AP excludes W/H tax (posted to 12810).
/// </summary>
public static class PurchaseApBalance
{
    private const int LiabilityTypeId = 2;

    public static bool UsesQuickBooksSignedPresentation(int companyId, string? accountNumber) =>
        PurchaseWithholdingTaxLayout.SupportsPurchaseWithholdingTax(companyId)
        && string.Equals(accountNumber, AccountsPayable, StringComparison.OrdinalIgnoreCase);

    public static decimal ComputeStoredNet(
        decimal openingBalance,
        decimal journalDebits,
        decimal journalCredits) =>
        GlAccountBalance.ComputeNet(
            openingBalance,
            journalDebits,
            journalCredits,
            LiabilityTypeId,
            AccountsPayable);

    /// <summary>QB signed AP balance (negative = owed). Excludes W/H tax on 12810.</summary>
    public static decimal ToSignedDisplay(
        decimal openingBalance,
        decimal journalDebits,
        decimal journalCredits) =>
        ToSignedDisplayFromStoredNet(
            openingBalance,
            ComputeStoredNet(openingBalance, journalDebits, journalCredits));

    /// <summary>QB signed AP from stored credit-normal net (negate so negative = owed).</summary>
    public static decimal ToSignedDisplayFromStoredNet(decimal _, decimal storedNet) =>
        Math.Round(-storedNet, 2);

    public static bool UsesInvertedLineAccumulation(int companyId, string? accountNumber) =>
        UsesQuickBooksSignedPresentation(companyId, accountNumber)
        || GlBalanceDisplay.UsesInvertedLineAccumulation(LiabilityTypeId, accountNumber, companyId);
}
