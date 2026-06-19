namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// Purchase-side income tax withholding for selected companies.
/// </summary>
public static class PurchaseWithholdingTaxLayout
{
    public static readonly int[] PurchaseWithholdingTaxCompanyIds = [2, 4, 5, 6, 7];

    public const decimal DefaultWithholdingTaxRate = 1m;
    public const string SectionCode = "153(1)(a)";
    public const string SectionLabel = "Payment for Goods u/s 153(1)(a)";
    public const string NatureOfPayment = "Withheld income tax adjustable";

    public const decimal DefaultIncomeTax236GRate = 0.10m;
    public const string IncomeTax236GSectionCode = "236G";
    public const string IncomeTax236GSectionLabel = "Income Tax u/s 236G";

    public static bool SupportsPurchaseWithholdingTax(int companyId) =>
        PurchaseWithholdingTaxCompanyIds.Contains(companyId);

    public static decimal SuggestTaxAmount(decimal taxableSubTotal, decimal rate) =>
        rate <= 0m ? 0m : Math.Round(taxableSubTotal * rate / 100m, 2);
}
