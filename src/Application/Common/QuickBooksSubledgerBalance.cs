namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// QuickBooks vendor balance reports use negative amounts when the company owes the vendor.
/// ERP stores vendor/AP openings as positive amounts owed.
/// </summary>
public static class QuickBooksSubledgerBalance
{
    public static decimal NormalizeVendorOpeningFromQuickBooks(decimal quickBooksBalance) =>
        quickBooksBalance < 0m ? Math.Round(-quickBooksBalance, 2) : Math.Round(quickBooksBalance, 2);

    public static decimal NormalizeVendorOpeningForControlAccount(decimal vendorOpeningBalance) =>
        NormalizeVendorOpeningFromQuickBooks(vendorOpeningBalance);
}
