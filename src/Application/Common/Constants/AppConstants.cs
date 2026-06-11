namespace PakistanAccountingERP.Application.Common.Constants;

public static class AppConstants
{
    public const string DefaultCurrency = "PKR";
    public const string DateFormat = "dd/MM/yyyy";
    public const string CustomerIdPrefix = "CUST-";
    public const string VendorCodePrefix = "VEND-";
    public const string InvoiceNumberPrefix = "INV-";
    public const string ReceiptNumberPrefix = "RCP-";
    public const string ItemCodePrefix = "ITEM-";
    public const string JournalEntryNumberPrefix = "JE-";
    public const string WarehouseCodePrefix = "WH-";
    public const string StockTransactionReferencePrefix = "STK-";
    public const string VendorBillNumberPrefix = "BILL-";
    public const string VendorPaymentNumberPrefix = "VPAY-";
}

public static class SessionKeys
{
    public const string CompanyId = "CurrentCompanyId";
    public const string CompanyName = "CurrentCompanyName";
    public const string CompanyLocked = "CompanySessionLocked";
}

public static class ReferenceTypes
{
    public const string Customer = "Customer";
    public const string Vendor = "Vendor";
    public const string SalesInvoice = "SalesInvoice";
    public const string VendorBill = "VendorBill";
    public const string BankTransaction = "BankTransaction";
    public const string JournalEntry = "JournalEntry";
    public const string CustomerReceipt = "CustomerReceipt";
    public const string VendorPayment = "VendorPayment";
    public const string Bank = "Bank";
    public const string BankReconciliation = "BankReconciliation";
    public const string CompanySettings = "CompanySettings";
    public const string Company = "Company";
    public const string Item = "Item";
    public const string ItemCategory = "ItemCategory";
    public const string UnitOfMeasure = "UnitOfMeasure";
    public const string Warehouse = "Warehouse";
    public const string InventoryTransaction = "InventoryTransaction";
    public const string Manual = "Manual";
}
