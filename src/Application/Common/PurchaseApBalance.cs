using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;

namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// QuickBooks AP presentation for purchase-tax companies (2, 4, 5, 6, 7).
/// QB account ledgers show negative balances when the company owes vendors.
/// AP excludes W/H tax (posted to 12810); signed AP = -opening + AP credits - debits.
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
        Math.Round(-openingBalance + journalCredits - journalDebits, 2);

    /// <summary>QB signed AP from stored net and chart opening.</summary>
    public static decimal ToSignedDisplayFromStoredNet(decimal openingBalance, decimal storedNet) =>
        Math.Round(storedNet - (2m * openingBalance), 2);

    public static bool UsesInvertedLineAccumulation(int companyId, string? accountNumber) =>
        UsesQuickBooksSignedPresentation(companyId, accountNumber)
        || GlBalanceDisplay.UsesInvertedLineAccumulation(LiabilityTypeId, accountNumber);
}
