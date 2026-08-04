using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;

namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// Computes the Opening Balance Equity (30000) opening amount that makes the trial balance balance.
/// Must subtract OBE's own journal activity (e.g. customer/vendor opening offsets), otherwise
/// replug leaves a residual equal to those journal credits/debits.
/// </summary>
public static class OpeningBalanceEquityBalancer
{
    public readonly record struct AccountTotals(
        int AccountId,
        decimal OpeningBalance,
        decimal JournalDebit,
        decimal JournalCredit,
        int? TypeId,
        string? AccountNumber);

    /// <summary>
    /// Required OBE opening so TB debits = credits, given all other accounts' closings and OBE journals.
    /// </summary>
    public static decimal ComputeRequiredOpening(
        IEnumerable<AccountTotals> otherAccounts,
        decimal obeJournalDebit,
        decimal obeJournalCredit,
        int companyId)
    {
        decimal closingDebits = 0m;
        decimal closingCredits = 0m;

        foreach (var account in otherAccounts)
        {
            var closingNet = GlAccountBalance.ComputeNet(
                account.OpeningBalance,
                account.JournalDebit,
                account.JournalCredit,
                account.TypeId,
                account.AccountNumber,
                companyId);
            var (debit, credit) = GlTrialBalanceColumns.SplitClosingBalance(
                closingNet,
                account.TypeId,
                account.AccountNumber,
                companyId);
            closingDebits += debit;
            closingCredits += credit;
        }

        // Gap = excess debits excluding OBE; OBE must show that amount on the credit side (or debit if negative).
        var gap = Math.Round(closingDebits - closingCredits, 2);
        // Equity credit-normal: closingNet = Opening + (JeCr - JeDr). Need closingNet = -gap.
        return Math.Round(-gap - (obeJournalCredit - obeJournalDebit), 2);
    }

    public static bool IsOpeningBalanceEquity(string? accountNumber) =>
        string.Equals(accountNumber, OpeningBalanceEquity, StringComparison.OrdinalIgnoreCase);
}
