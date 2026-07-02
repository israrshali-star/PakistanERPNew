using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;

namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// QuickBooks-style bank/cash ledger for every company:
/// closing = opening (Dr) + received (Dr) − paid (Cr).
/// </summary>
public static class BankLedgerBalance
{
    public const int AssetTypeId = 1;
    public const int CashAndBankSubTypeId = 1;

    public static bool UsesDebitMinusCreditLedger(
        int? typeId,
        int? subTypeId,
        bool isLinkedToBank,
        int? parentTypeId = null,
        int? parentSubTypeId = null,
        string? accountNumber = null,
        string? parentAccountNumber = null)
    {
        if (typeId != AssetTypeId)
        {
            return false;
        }

        if (IsCashOrBankAccountNumber(accountNumber))
        {
            return true;
        }

        if (string.Equals(parentAccountNumber, BankAccountsParent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (isLinkedToBank || subTypeId == CashAndBankSubTypeId)
        {
            return true;
        }

        return parentTypeId == AssetTypeId && parentSubTypeId == CashAndBankSubTypeId;
    }

    private static bool IsCashOrBankAccountNumber(string? accountNumber) =>
        string.Equals(accountNumber, CashInHand, StringComparison.OrdinalIgnoreCase)
        || string.Equals(accountNumber, KeptAside, StringComparison.OrdinalIgnoreCase)
        || string.Equals(accountNumber, UndepositedFunds, StringComparison.OrdinalIgnoreCase);

    public static decimal Accumulate(decimal balance, decimal debit, decimal credit) =>
        Math.Round(balance + debit - credit, 2);

    public static decimal ComputeClosing(decimal opening, decimal periodDebits, decimal periodCredits) =>
        Math.Round(opening + periodDebits - periodCredits, 2);
}
