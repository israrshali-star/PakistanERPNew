using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;

namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// Maps stored GL net balances to trial-balance debit/credit columns.
/// </summary>
public static class GlTrialBalanceColumns
{
    private const int LiabilityTypeId = 2;
    private const int EquityTypeId = 3;
    private const int RevenueTypeId = 4;

    public static (decimal Debit, decimal Credit) SplitClosingBalance(
        decimal storedNet,
        int? typeId,
        string? accountNumber)
    {
        var amount = Math.Abs(storedNet);
        if (amount == 0m)
        {
            return (0m, 0m);
        }

        return UsesDebitColumn(storedNet, typeId, accountNumber)
            ? (amount, 0m)
            : (0m, amount);
    }

    public static bool UsesDebitColumn(decimal storedNet, int? typeId, string? accountNumber)
    {
        if (string.Equals(accountNumber, AccountsReceivable, StringComparison.OrdinalIgnoreCase))
        {
            return storedNet < 0m;
        }

        if (string.Equals(accountNumber, AccountsPayable, StringComparison.OrdinalIgnoreCase))
        {
            // AP is stored positive when owed; credit column is the normal payable side.
            return storedNet < 0m;
        }

        if (string.Equals(accountNumber, OpeningBalanceEquity, StringComparison.OrdinalIgnoreCase))
        {
            return storedNet > 0m;
        }

        if (typeId == LiabilityTypeId)
        {
            return storedNet > 0m;
        }

        if (typeId is EquityTypeId or RevenueTypeId)
        {
            return storedNet < 0m;
        }

        return storedNet > 0m;
    }
}
