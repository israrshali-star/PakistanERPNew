namespace PakistanAccountingERP.Application.Common.Constants;

/// <summary>
/// Standard chart-of-account numbers used for GL posting.
/// </summary>
public static class GlAccountNumbers
{
    public const string CashInHand = "10015";
    public const string KeptAside = "10016";
    public const string UndepositedFunds = "10017";
    public const string BankAccountsParent = "10000";
    public const string AccountsReceivableParent = "11000";
    public const string AccountsReceivable = "11110";
    public const string InventoryAsset = "12110";
    public const string PrepaidSalesTax = "12910";
    public const string FixedAssets = "1500";
    public const string AccountsPayable = "20000";
    public const string AccruedLiabilities = "2300";
    public const string SalesTaxPayable = "25500";
    public const string CartagePayable = "26100";
    public const string OwnersCapital = "3100";
    public const string OpeningBalanceEquity = "30000";
    public const string RetainedEarnings = "32010";
    public const string SalesRevenue = "47910";
    public const string SalesReturns = "4200";
    public const string CostOfGoodsSold = "50000";
    public const string Purchases = "5100";
    public const string FreightIn = "5200";
    public const string AdministrativeExpenses = "6100";
    public const string SellingAndMarketing = "6200";
    public const string PayrollAndBenefits = "6300";

    /// <summary>Legacy account numbers remapped to <see cref="AccountsReceivable"/> etc.</summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyRemap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["1200"] = AccountsReceivable,
        ["1300"] = InventoryAsset,
        ["1400"] = PrepaidSalesTax,
        ["2100"] = AccountsPayable,
        ["2200"] = SalesTaxPayable,
        ["4100"] = SalesRevenue,
        ["1100"] = CashInHand,
        ["3200"] = RetainedEarnings
    };
}
