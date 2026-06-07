namespace PakistanAccountingERP.Application.DTOs;

public sealed record QuickBooksIifImportResult(
    bool Success,
    string Message,
    int AccountsImported = 0,
    int ItemsImported = 0,
    int CustomersImported = 0,
    int VendorsImported = 0,
    int AccountsSkipped = 0,
    int ItemsSkipped = 0,
    int CustomersSkipped = 0,
    int VendorsSkipped = 0,
    int InvoicesImported = 0,
    int BillsImported = 0,
    int CustomerReceiptsImported = 0,
    int VendorPaymentsImported = 0,
    int CustomerBalancesUpdated = 0,
    int VendorBalancesUpdated = 0,
    int InvoicesSkipped = 0,
    int BillsSkipped = 0);
