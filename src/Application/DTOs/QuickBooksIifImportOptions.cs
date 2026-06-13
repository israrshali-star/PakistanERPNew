namespace PakistanAccountingERP.Application.DTOs;

public sealed class QuickBooksIifImportOptions
{
    public string? CustomerBalancesCsvPath { get; set; }
    public string? VendorBalancesCsvPath { get; set; }
    public DateTime? CutoverDate { get; set; }
    public string? OpenInvoicesCsvPath { get; set; }
    public string? OpenBillsCsvPath { get; set; }
    public string? InventoryValuationCsvPath { get; set; }
    public bool SkipMasterData { get; set; }

    /// <summary>
    /// When true, opening stock import records stack/lot/qty/cartons only.
    /// Bill and inventory transactions use zero rates/amounts; bill stays Draft (no GL).
    /// </summary>
    public bool OpeningStockQuantityOnly { get; set; }
}
