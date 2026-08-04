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
        string? accountNumber,
        int? companyId = null) =>
        openingBalance + GetJournalDelta(journalDebits, journalCredits, typeId, accountNumber, companyId);

    public static decimal ComputeNet(
        decimal openingBalance,
        decimal journalNetDebitMinusCredit,
        int? typeId,
        string? accountNumber,
        int? companyId = null) =>
        UsesCreditMinusDebitJournalDelta(typeId, accountNumber, companyId)
            ? openingBalance - journalNetDebitMinusCredit
            : openingBalance + journalNetDebitMinusCredit;

    public static decimal GetJournalDelta(
        decimal journalDebits,
        decimal journalCredits,
        int? typeId,
        string? accountNumber,
        int? companyId = null) =>
        UsesCreditMinusDebitJournalDelta(typeId, accountNumber, companyId)
            ? journalCredits - journalDebits
            : journalDebits - journalCredits;

    /// <summary>
    /// Credit-minus-debit journal delta for credit-normal stored accounts:
    /// equity, AR (inverted asset), AP (ERP positive-payable storage), and — for companies
    /// 2/4/5/6/7 — sales tax liabilities so invoice tax credits reduce the payable balance.
    /// Other liabilities keep debit-minus-credit.
    /// </summary>
    public static bool UsesCreditMinusDebitJournalDelta(
        int? typeId,
        string? accountNumber,
        int? companyId = null) =>
        typeId == EquityTypeId
        || string.Equals(accountNumber, AccountsReceivable, StringComparison.OrdinalIgnoreCase)
        || string.Equals(accountNumber, AccountsPayable, StringComparison.OrdinalIgnoreCase)
        || (companyId.HasValue
            && TradeInvoiceLayout.InvoiceCreditsReduceSalesTaxBalance(companyId.Value)
            && GlOpeningBalanceNormalizer.IsSalesTaxLiabilityAccount(accountNumber));

    /// <summary>
    /// AP dashboard closing for purchase-tax companies: opening + debits − credits.
    /// </summary>
    public static decimal ComputeDebitMinusCreditClosing(
        decimal openingBalance,
        decimal journalDebits,
        decimal journalCredits) =>
        Math.Round(openingBalance + journalDebits - journalCredits, 2);
}
