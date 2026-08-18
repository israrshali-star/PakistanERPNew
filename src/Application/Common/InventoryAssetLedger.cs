using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;

namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// Inventory asset (12110) ledger: Closing = Opening + Bills (Dr) − Invoices (Cr).
/// </summary>
public static class InventoryAssetLedger
{
    public static bool UsesOpeningPlusBillsMinusInvoices(string? accountNumber) =>
        string.Equals(accountNumber, InventoryAsset, StringComparison.OrdinalIgnoreCase);

    public static decimal ComputeClosing(decimal opening, decimal billDebits, decimal invoiceCredits) =>
        Math.Round(opening + billDebits - invoiceCredits, 2);
}
