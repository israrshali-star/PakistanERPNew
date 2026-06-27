using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;

namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// Normalizes opening balances to credit-normal liability / debit-normal AR storage.
/// </summary>
public static class GlOpeningBalanceNormalizer
{
    private const int AssetTypeId = 1;
    private const int LiabilityTypeId = 2;

    public static decimal NormalizeForStorage(decimal openingBalance, int? typeId, string? accountNumber)
    {
        if (openingBalance == 0m || !typeId.HasValue)
        {
            return Math.Round(openingBalance, 2);
        }

        if (typeId.Value == LiabilityTypeId
            && IsSalesTaxLiabilityAccount(accountNumber)
            && openingBalance > 0m)
        {
            return Math.Round(-openingBalance, 2);
        }

        if (typeId.Value == LiabilityTypeId && openingBalance > 0m)
        {
            return Math.Round(-openingBalance, 2);
        }

        if (typeId.Value == AssetTypeId
            && string.Equals(accountNumber, AccountsReceivable, StringComparison.OrdinalIgnoreCase)
            && openingBalance > 0m)
        {
            return Math.Round(-openingBalance, 2);
        }

        return Math.Round(openingBalance, 2);
    }

    public static bool IsSalesTaxLiabilityAccount(string? accountNumber) =>
        string.Equals(accountNumber, SalesTaxPayable, StringComparison.OrdinalIgnoreCase)
        || string.Equals(accountNumber, FurtherTaxPayable, StringComparison.OrdinalIgnoreCase)
        || string.Equals(accountNumber, SalesTaxPayable18, StringComparison.OrdinalIgnoreCase);
}
