namespace PakistanAccountingERP.Application.DTOs;

public sealed class QuickBooksIifImportOptions
{
    public string? CustomerBalancesCsvPath { get; set; }
    public string? VendorBalancesCsvPath { get; set; }
    public string? OpenInvoicesCsvPath { get; set; }
    public string? OpenBillsCsvPath { get; set; }
    public string? InventoryValuationCsvPath { get; set; }
    public bool SkipMasterData { get; set; }
}
