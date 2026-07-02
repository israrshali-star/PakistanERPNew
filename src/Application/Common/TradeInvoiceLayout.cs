using System.Globalization;

using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Common;

public static class TradeInvoiceLayout
{
    public const int TradeInvoiceCompanyId = 3;

    /// <summary>Companies that post SN002 sales tax to 25520 (18%) and 25510 (4%) with 25500 as parent total.</summary>
    public static readonly int[] SplitTaxGlCompanyIds = [2, 3, 4, 5, 6, 7];

    /// <summary>Companies that support bulk PDF print of FBR-submitted invoices from the list page.</summary>
    public static readonly int[] BulkInvoicePrintCompanyIds = [2, 4, 5, 6, 7];

    public static bool SupportsBulkInvoicePrint(int companyId) =>
        BulkInvoicePrintCompanyIds.Contains(companyId);

    public static bool SupportsGodownChallanEmail(int companyId) =>
        companyId == TradeInvoiceCompanyId;

    /// <summary>Company 3: unregistered SN002 tax split (18% + 4%) at invoice footer, not per line.</summary>
    public static bool UsesUnregisteredBillLevelTaxSplit(int companyId) =>
        companyId == TradeInvoiceCompanyId;

    public static bool UsesSplitTaxSubAccounts(int companyId) =>
        SplitTaxGlCompanyIds.Contains(companyId);

    public static CultureInfo NumberCulture { get; } = CultureInfo.GetCultureInfo("en-PK");

    public static string FormatAmount(decimal value) =>
        value.ToString("N2", NumberCulture);

    public static string FormatTaxRate(decimal taxRate) =>
        taxRate.ToString("0.0", NumberCulture);

    /// <summary>Tax rate with up to 2 decimals, trailing zeros trimmed (e.g. 20.85, 19, 18).</summary>
    public static string FormatTaxRatePrecise(decimal taxRate) =>
        taxRate.ToString("0.##", NumberCulture);

    public static string BuildDescription(
        string? productDescription,
        string? itemDescription,
        string? lotNo,
        string? stackNo)
    {
        var baseDescription = !string.IsNullOrWhiteSpace(productDescription)
            ? productDescription.Trim()
            : itemDescription;

        return FbrInvoiceLayout.BuildFbrProductDescription(baseDescription, lotNo, stackNo);
    }

    public static decimal LineAmountExTax(decimal quantity, decimal price, decimal discount) =>
        Math.Round(Math.Max(0m, quantity * price - discount), 2);

    /// <summary>Goods weight/carton totals exclude cartage and service charge lines.</summary>
    public static bool CountsTowardWeightAndCartonTotals(ItemType itemType, string? itemCode) =>
        !SalesTaxSplit.IsCartageOrService(itemType, itemCode);

    public static decimal ResolveTaxRateDisplay(decimal taxableTotal, decimal taxAmount, IReadOnlyList<decimal> lineTaxRates)
    {
        var uniformRate = TryGetUniformPositiveTaxRate(lineTaxRates);
        if (uniformRate.HasValue)
        {
            return Math.Round(uniformRate.Value, 1);
        }

        if (taxableTotal > 0m)
        {
            return Math.Round(taxAmount / taxableTotal * 100m, 1);
        }

        return lineTaxRates.Count > 0 ? lineTaxRates[0] : 0m;
    }

    /// <summary>
    /// Combined sales-tax + further-tax rate for display/printing, e.g. 18% sales tax + 1% further tax = 19%.
    /// Rates are derived from the actual posted amounts (header is authoritative) so a stale line
    /// tax rate cannot distort the printed figure.
    /// </summary>
    public static decimal ResolveCombinedTaxRateDisplay(
        decimal taxableTotal,
        decimal salesTaxAmount,
        decimal furtherTaxAmount,
        IReadOnlyList<decimal> lineTaxRates)
    {
        if (taxableTotal > 0m)
        {
            var salesRate = salesTaxAmount / taxableTotal * 100m;
            var furtherRate = furtherTaxAmount / taxableTotal * 100m;
            return Math.Round(salesRate + furtherRate, 2);
        }

        return ResolveTaxRateDisplay(taxableTotal, salesTaxAmount + furtherTaxAmount, lineTaxRates);
    }

    /// <summary>
    /// When goods and cartage/service lines share one positive tax rate (e.g. 22% goods, 0% cartage),
    /// show the statutory rate instead of a diluted effective rate from invoice totals.
    /// </summary>
    private static decimal? TryGetUniformPositiveTaxRate(IReadOnlyList<decimal> lineTaxRates)
    {
        decimal? rate = null;
        foreach (var lineRate in lineTaxRates)
        {
            if (lineRate <= 0m)
            {
                continue;
            }

            if (rate is null)
            {
                rate = lineRate;
            }
            else if (rate.Value != lineRate)
            {
                return null;
            }
        }

        return rate;
    }
}
