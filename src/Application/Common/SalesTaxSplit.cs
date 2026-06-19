namespace PakistanAccountingERP.Application.Common;

using PakistanAccountingERP.Domain.Enums;

public static class SalesTaxSplit
{
    public const string UnregisteredScenarioCode = "SN002";
    public const string CartageItemCode = "ITEM-0002";

    public static bool IsUnregisteredScenario(string? scenarioCode) =>
        string.Equals(scenarioCode, UnregisteredScenarioCode, StringComparison.OrdinalIgnoreCase);

    public static bool IsCartageOrService(ItemType itemType, string? itemCode) =>
        itemType == ItemType.Service
        || string.Equals(itemCode, CartageItemCode, StringComparison.OrdinalIgnoreCase);

    public static decimal ComputeGoodsTaxable(IEnumerable<(decimal Quantity, decimal Price, decimal Discount)> lines) =>
        Math.Round(lines.Sum(line => ComputeLineTaxable(line.Quantity, line.Price, line.Discount)), 2);

    public static decimal ComputeLineTaxable(decimal quantity, decimal price, decimal discount)
    {
        var lineSubTotal = Math.Round(quantity * price, 2);
        var lineDiscount = Math.Round(Math.Max(0m, discount), 2);
        return Math.Round(lineSubTotal - lineDiscount, 2);
    }

    public static decimal FurtherTaxRate(decimal registeredRate, decimal unregisteredRate) =>
        Math.Max(0m, unregisteredRate - registeredRate);

    /// <summary>
    /// Further tax (4% / 2%) applies only when the line tax rate exceeds the registered sales tax rate (18%).
    /// A line at 18% is registered-rate only; unregistered SN002 lines at 22% split into 18% + further tax.
    /// </summary>
    public static bool AppliesFurtherTax(decimal lineTaxRate, decimal registeredRate) =>
        lineTaxRate > registeredRate + 0.001m;

    public static bool ApplyFurtherTaxForLine(
        bool isUnregisteredScenario,
        decimal lineTaxRate,
        decimal registeredRate) =>
        isUnregisteredScenario && AppliesFurtherTax(lineTaxRate, registeredRate);

    public static (decimal SalesTax, decimal FurtherTax, decimal TotalTax) CalculateBillTax(
        IEnumerable<(decimal Taxable, decimal LineTaxRate)> lines,
        decimal registeredRate,
        decimal unregisteredRate,
        decimal defaultFurtherRate,
        decimal? furtherTaxRate,
        decimal? furtherTaxAmount)
    {
        decimal salesTaxTotal = 0m;
        decimal furtherTaxTotal = 0m;

        foreach (var (taxable, lineTaxRate) in lines)
        {
            if (taxable <= 0m)
            {
                continue;
            }

            var applyFurtherTax = AppliesFurtherTax(lineTaxRate, registeredRate);
            var (salesTax, furtherTax, _) = CalculateLineTax(
                taxable,
                registeredRate,
                unregisteredRate,
                applyFurtherTax,
                furtherTaxRate ?? defaultFurtherRate,
                lineTaxRate);
            salesTaxTotal += salesTax;
            furtherTaxTotal += furtherTax;
        }

        if (furtherTaxAmount.HasValue)
        {
            furtherTaxTotal = Math.Round(Math.Max(0m, furtherTaxAmount.Value), 2);
        }

        salesTaxTotal = Math.Round(salesTaxTotal, 2);
        furtherTaxTotal = Math.Round(furtherTaxTotal, 2);
        return (salesTaxTotal, furtherTaxTotal, salesTaxTotal + furtherTaxTotal);
    }

    /// <summary>Bill-level SN002 split on a single goods taxable total (legacy bill footer).</summary>
    public static (decimal SalesTax, decimal FurtherTax, decimal TotalTax) CalculateBillTax(
        decimal goodsTaxableTotal,
        decimal registeredRate,
        decimal defaultFurtherRate,
        decimal? furtherTaxRate,
        decimal? furtherTaxAmount)
    {
        if (goodsTaxableTotal <= 0m)
        {
            return (0m, 0m, 0m);
        }

        var salesTax = Math.Round(goodsTaxableTotal * registeredRate / 100m, 2);
        decimal furtherTax;
        if (furtherTaxAmount.HasValue)
        {
            furtherTax = Math.Round(Math.Max(0m, furtherTaxAmount.Value), 2);
        }
        else
        {
            var rate = furtherTaxRate ?? defaultFurtherRate;
            furtherTax = Math.Round(goodsTaxableTotal * rate / 100m, 2);
        }

        return (salesTax, furtherTax, salesTax + furtherTax);
    }

    public static (decimal SalesTax, decimal FurtherTax, decimal TotalTax) CalculateInvoiceTax(
        decimal taxableTotal,
        decimal registeredRate,
        decimal unregisteredRate,
        bool isUnregisteredScenario,
        decimal? furtherTaxRateOverride = null,
        decimal? lineTaxRate = null) =>
        CalculateLineTax(
            taxableTotal,
            registeredRate,
            unregisteredRate,
            isUnregisteredScenario,
            furtherTaxRateOverride,
            lineTaxRate);

    public static (decimal SalesTax, decimal FurtherTax, decimal TotalTax) CalculateLineTax(
        decimal taxable,
        decimal registeredRate,
        decimal unregisteredRate,
        bool applyFurtherTax,
        decimal? furtherTaxRateOverride = null,
        decimal? lineTaxRate = null)
    {
        if (taxable <= 0m)
        {
            return (0m, 0m, 0m);
        }

        var effectiveLineRate = lineTaxRate ?? (applyFurtherTax ? unregisteredRate : registeredRate);

        if (!applyFurtherTax)
        {
            var rate = effectiveLineRate > 0m ? effectiveLineRate : registeredRate;
            var tax = Math.Round(taxable * rate / 100m, 2);
            return (tax, 0m, tax);
        }

        var salesTax = Math.Round(taxable * registeredRate / 100m, 2);
        var furtherRate = furtherTaxRateOverride
            ?? FurtherTaxRate(registeredRate, unregisteredRate);
        var furtherTax = Math.Round(taxable * furtherRate / 100m, 2);
        return (salesTax, furtherTax, salesTax + furtherTax);
    }
}
