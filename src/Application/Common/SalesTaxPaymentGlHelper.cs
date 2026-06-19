using PakistanAccountingERP.Application.Common.Constants;
using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;

namespace PakistanAccountingERP.Application.Common;

public static class SalesTaxPaymentGlHelper
{
    public static bool IsSalesTaxAccountNumber(string? accountNumber) =>
        string.Equals(accountNumber, SalesTaxPayable, StringComparison.OrdinalIgnoreCase)
        || string.Equals(accountNumber, FurtherTaxPayable, StringComparison.OrdinalIgnoreCase)
        || string.Equals(accountNumber, SalesTaxPayable18, StringComparison.OrdinalIgnoreCase);

    public static bool IsSalesTaxPartyName(string? partyName) =>
        !string.IsNullOrWhiteSpace(partyName)
        && (partyName.Contains("Sales Tax", StringComparison.OrdinalIgnoreCase)
            || partyName.Contains("Used Tax", StringComparison.OrdinalIgnoreCase));

    public static bool ShouldSplitPaymentToSubAccounts(int companyId, string? counterAccountNumber) =>
        TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId)
        && (string.Equals(counterAccountNumber, SalesTaxPayable, StringComparison.OrdinalIgnoreCase)
            || counterAccountNumber is null);

    /// <summary>Credit-normal liability: outstanding payable is the positive amount owed.</summary>
    public static decimal LiabilityOutstanding(decimal netBalance) =>
        netBalance < 0m ? Math.Round(-netBalance, 2) : 0m;

    /// <summary>Allocate a payment across 4% (25510) first, then 18% (25520).</summary>
    public static (decimal FurtherTaxAmount, decimal SalesTax18Amount) AllocatePayment(
        decimal paymentAmount,
        decimal furtherTaxOutstanding,
        decimal salesTax18Outstanding)
    {
        paymentAmount = Math.Round(paymentAmount, 2);
        if (paymentAmount <= 0m)
        {
            return (0m, 0m);
        }

        var furtherPay = Math.Min(paymentAmount, furtherTaxOutstanding);
        var remaining = Math.Round(paymentAmount - furtherPay, 2);
        var salesTax18Pay = Math.Min(remaining, salesTax18Outstanding);
        remaining = Math.Round(remaining - salesTax18Pay, 2);

        if (remaining > 0m)
        {
            salesTax18Pay = Math.Round(salesTax18Pay + remaining, 2);
        }

        return (furtherPay, salesTax18Pay);
    }
}
