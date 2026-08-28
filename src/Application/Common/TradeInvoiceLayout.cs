using System.Globalization;

using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Common;

public static class TradeInvoiceLayout
{
    public const int TradeInvoiceCompanyId = 3;

    /// <summary>
    /// Companies that post sales tax to 25520 (18%) and 25510 (4% further tax) with 25500 as parent rollup.
    /// Applies to registered (SN001 / 18% only) and unregistered (SN002 / 18%+4%) invoices.
    /// </summary>
    public static readonly int[] SplitTaxGlCompanyIds = [2, 3, 4, 5, 6, 7];

    /// <summary>Companies that support bulk PDF print of FBR-submitted invoices from the list page.</summary>
    public static readonly int[] BulkInvoicePrintCompanyIds = [2, 4, 5, 6, 7];

    /// <summary>
    /// Companies where sales tax payable (25500/25510/25520) display balance is reduced
    /// when a sales invoice posts tax credits (opening credit − invoice tax).
    /// </summary>
    public static readonly int[] InvoiceCreditsReduceSalesTaxCompanyIds = BulkInvoicePrintCompanyIds;

    /// <summary>Kashaf Polyester — invoice numbers use INV-001 (3 digits), not INV-0001.</summary>
    public const int KashafPolyesterCompanyId = 5;

    /// <summary>Company 3 (MIA) can share customer/vendor ledgers and customer receipts with Urdu PDF labels.</summary>
    public static bool SupportsUrduLedger(int companyId) =>
        companyId == TradeInvoiceCompanyId;

    /// <summary>Company 3 (MIA) shows remaining balance and FIFO invoice allocation on customer receipts.</summary>
    public static bool ShowsCustomerReceiptInvoiceAllocation(int companyId) =>
        companyId == TradeInvoiceCompanyId;

    /// <summary>Company 3 (MIA) prints customer receipts on A4 landscape.</summary>
    public static bool UsesLandscapeCustomerReceipt(int companyId) =>
        companyId == TradeInvoiceCompanyId;

    /// <summary>Max receipt attachments for a company; null means use the global Attachments config default.</summary>
    public static int? GetCustomerReceiptAttachmentLimit(int companyId) =>
        companyId == TradeInvoiceCompanyId ? 2 : null;

    /// <summary>Company 3 (MIA) may write cheques / withdrawals even when the pay-from bank GL balance is insufficient.</summary>
    public static bool AllowsInsufficientBankBalanceForCheques(int companyId) =>
        companyId == TradeInvoiceCompanyId;

    /// <summary>Company 3 (MIA) has a stock movement report grouped by stack number.</summary>
    public static bool SupportsStackMovementReport(int companyId) =>
        companyId == TradeInvoiceCompanyId;

    /// <summary>Company 3 (MIA) has a monthly vendor payment report with FIFO bill Ref # allocation.</summary>
    public static bool SupportsVendorPaymentRefReport(int companyId) =>
        companyId == TradeInvoiceCompanyId;

    /// <summary>
    /// Related-company / clearing vendors omitted from the company 3 vendor payment Ref # report.
    /// </summary>
    private static readonly string[] VendorPaymentReportExcludedNameFragments =
    [
        "Sales Tax & Used Tax",
        "Yarn Merchants",
        "Al-Aziz",
        "Al Baasit",
        "Al Wahhab",
        "Kashaf Polyester",
        "Arian Traders"
    ];

    public static bool IsExcludedFromVendorPaymentRefReport(string? vendorName)
    {
        if (string.IsNullOrWhiteSpace(vendorName))
        {
            return false;
        }

        foreach (var fragment in VendorPaymentReportExcludedNameFragments)
        {
            if (vendorName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Related-group companies omitted from Purchase by Vendor (Al Baasit, Al-Aziz, Arian, Kashaf, Al Wahhab).
    /// </summary>
    private static readonly string[] PurchaseByVendorExcludedNameTokens =
    [
        "albaasit",
        "albasit",
        "alaziz",
        "arian",
        "kashaf",
        "alwahhab"
    ];

    public static bool IsExcludedFromPurchaseByVendorReport(string? vendorName) =>
        MatchesAnyToken(vendorName, PurchaseByVendorExcludedNameTokens);

    /// <summary>
    /// Related-group companies shown on Related Company Purchase
    /// (Al Baasit, Arian Traders, Kashaf Polyester, Al Wahhab Merchants).
    /// </summary>
    private static readonly string[] RelatedCompanyPurchaseNameTokens =
    [
        "albaasit",
        "albasit",
        "arian",
        "kashaf",
        "alwahhab"
    ];

    public static bool IsIncludedInRelatedCompanyPurchaseReport(string? vendorName) =>
        MatchesAnyToken(vendorName, RelatedCompanyPurchaseNameTokens);

    private static bool MatchesAnyToken(string? vendorName, string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(vendorName))
        {
            return false;
        }

        var normalized = new string(vendorName.Where(char.IsLetter).ToArray())
            .ToLowerInvariant();

        foreach (var token in tokens)
        {
            if (normalized.Contains(token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Company 3 (MIA) omits vendor ref from Stock Summary because lots span many stacks.</summary>
    public static bool ShowsStockSummaryVendorRef(int companyId) =>
        companyId != TradeInvoiceCompanyId;

    /// <summary>
    /// Companies that must submit FBR seller/buyer NTN without the check digit after '-'.
    /// Example: 1234567-8 → 1234567.
    /// </summary>
    public static readonly int[] FbrNtnWithoutCheckDigitCompanyIds = BulkInvoicePrintCompanyIds;

    public static bool SupportsBulkInvoicePrint(int companyId) =>
        BulkInvoicePrintCompanyIds.Contains(companyId);

    public static bool InvoiceCreditsReduceSalesTaxBalance(int companyId) =>
        InvoiceCreditsReduceSalesTaxCompanyIds.Contains(companyId);

    public static bool UsesFbrNtnWithoutCheckDigit(int companyId) =>
        FbrNtnWithoutCheckDigitCompanyIds.Contains(companyId);

    /// <summary>
    /// Recalculate FBR line sales tax with round-half-up (AwayFromZero) so FBR validation matches.
    /// Company 3 (MIA) keeps ERP-stored line tax amounts in the FBR payload.
    /// </summary>
    public static bool UsesFbrAlignedSalesTaxRounding(int companyId) =>
        companyId != TradeInvoiceCompanyId;

    /// <summary>Digit pad width for auto invoice numbers (INV-001 vs INV-0001).</summary>
    public static int InvoiceNumberPadWidth(int companyId) =>
        companyId == KashafPolyesterCompanyId ? 3 : 4;

    public static string FormatInvoiceNumber(int sequence, int companyId) =>
        $"{AppConstants.InvoiceNumberPrefix}{sequence.ToString($"D{InvoiceNumberPadWidth(companyId)}")}";

    /// <summary>
    /// For selected companies, FBR expects NTN without check digit (3816161-3 → 3816161, I991816-7 → I991816).
    /// Only NTN check-digit suffixes are removed; CNIC values (34101-8988500-5) are left intact.
    /// </summary>
    public static string? NormalizeNtnForFbr(string? ntn, int companyId)
    {
        if (string.IsNullOrWhiteSpace(ntn))
        {
            return ntn;
        }

        var trimmed = ntn.Trim();
        if (!UsesFbrNtnWithoutCheckDigit(companyId))
        {
            return trimmed;
        }

        if (IsHyphenatedCnic(trimmed))
        {
            return trimmed;
        }

        // NTN check digit: PREFIX + (-|.) + single digit (e.g. 3816161-3 / 2733531.3 / I991816-7)
        var sepIndex = trimmed.LastIndexOfAny(['-', '.']);
        if (sepIndex > 0
            && sepIndex == trimmed.Length - 2
            && char.IsDigit(trimmed[^1]))
        {
            var head = trimmed.AsSpan(0, sepIndex);
            if (head.Length is >= 6 and <= 8 && IsLettersOrDigits(head))
            {
                return trimmed[..sepIndex];
            }
        }

        return trimmed;
    }

    /// <summary>CNIC with hyphens: #####-#######-# (15 chars).</summary>
    private static bool IsHyphenatedCnic(string value) =>
        value.Length == 15
        && value[5] == '-'
        && value[13] == '-'
        && char.IsDigit(value[14])
        && IsDigits(value.AsSpan(0, 5))
        && IsDigits(value.AsSpan(6, 7));

    private static bool IsDigits(ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLettersOrDigits(ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsLetterOrDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }

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
