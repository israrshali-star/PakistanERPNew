using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;

namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// Computes stored GL net balances from openings and journal totals.
/// QuickBooks import stores AR and liability balances with credit-normal signs.
/// </summary>
public static class GlAccountBalance
{
    private const int AssetTypeId = 1;
    private const int LiabilityTypeId = 2;
    private const int EquityTypeId = 3;

    public static decimal ComputeNet(
        decimal openingBalance,
        decimal journalDebits,
        decimal journalCredits,
        int? typeId,
        string? accountNumber) =>
        openingBalance + GetJournalDelta(journalDebits, journalCredits, typeId, accountNumber);

    public static decimal ComputeNet(
        decimal openingBalance,
        decimal journalNetDebitMinusCredit,
        int? typeId,
        string? accountNumber) =>
        UsesCreditMinusDebitJournalDelta(typeId, accountNumber)
            ? openingBalance - journalNetDebitMinusCredit
            : openingBalance + journalNetDebitMinusCredit;

    public static decimal GetJournalDelta(
        decimal journalDebits,
        decimal journalCredits,
        int? typeId,
        string? accountNumber) =>
        UsesCreditMinusDebitJournalDelta(typeId, accountNumber)
            ? journalCredits - journalDebits
            : journalDebits - journalCredits;

    public static bool UsesCreditMinusDebitJournalDelta(int? typeId, string? accountNumber) =>
        typeId is LiabilityTypeId or EquityTypeId;

    /// <summary>
    /// AP dashboard closing for purchase-tax companies: opening + debits − credits.
    /// </summary>
    public static decimal ComputeDebitMinusCreditClosing(
        decimal openingBalance,
        decimal journalDebits,
        decimal journalCredits) =>
        Math.Round(openingBalance + journalDebits - journalCredits, 2);
}
