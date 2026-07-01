namespace PakistanAccountingERP.Application.Import;

/// <summary>
/// Maps QuickBooks trial-balance account numbers to ERP chart numbers when they differ per company.
/// </summary>
public static class TrialBalanceAccountResolver
{
    private static readonly Dictionary<string, string> GlobalQbToErp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["10020"] = "10013",
        ["10800"] = "10015",
        ["10900"] = "10016",
        ["11000"] = "11110",
        ["12000"] = "10017",
        ["15200"] = "15100",
        ["30800"] = "30020",
        ["32000"] = "30000",
    };

    private static readonly Dictionary<int, Dictionary<string, string>> CompanyQbToErp =
        new()
        {
            // Al Baasit Trading — QB bank sub-accounts and owners equity
            [6] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["10110"] = "10010",
                ["10120"] = "10012",
                ["32000"] = "32000",
            },
            // Arian Traders
            [7] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["10110"] = "10001",
                ["10120"] = "10009",
            },
        };

    public static string ResolveQbAccountNumber(int companyId, string qbAccountNumber)
    {
        if (CompanyQbToErp.TryGetValue(companyId, out var companyMap)
            && companyMap.TryGetValue(qbAccountNumber, out var companyErp))
        {
            return companyErp;
        }

        if (GlobalQbToErp.TryGetValue(qbAccountNumber, out var globalErp))
        {
            return globalErp;
        }

        return qbAccountNumber;
    }

    public static string ResolveErpAccountNumber(int companyId, string erpAccountNumber) =>
        ResolveQbAccountNumber(companyId, erpAccountNumber);
}
