using System.Globalization;

namespace PakistanAccountingERP.Application.Common;

public static class TradeInvoiceLayout
{
    public const int TradeInvoiceCompanyId = 3;

    public static CultureInfo NumberCulture { get; } = CultureInfo.GetCultureInfo("en-PK");

    public static string FormatAmount(decimal value) =>
        value.ToString("N2", NumberCulture);

    public static string FormatTaxRate(decimal taxRate) =>
        taxRate.ToString("0.0", NumberCulture);

    public static string BuildDescription(
        string? productDescription,
        string? itemDescription,
        string? lotNo,
        string? stackNo)
    {
        if (!string.IsNullOrWhiteSpace(productDescription))
        {
            return productDescription.Trim();
        }

        return FbrInvoiceLayout.BuildFbrProductDescription(itemDescription, lotNo, stackNo);
    }

    public static decimal LineAmountExTax(decimal quantity, decimal price, decimal discount) =>
        Math.Round(Math.Max(0m, quantity * price - discount), 2);

    public static decimal ResolveTaxRateDisplay(decimal taxableTotal, decimal taxAmount, IReadOnlyList<decimal> lineTaxRates)
    {
        if (taxableTotal > 0m)
        {
            return Math.Round(taxAmount / taxableTotal * 100m, 1);
        }

        return lineTaxRates.Count > 0 ? lineTaxRates[0] : 0m;
    }
}
