using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Reports;

public static class CustomReportCatalog
{
    private static readonly Dictionary<string, CustomReportSourceDto> Sources =
        BuildSources().ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<CustomReportSourceDto> GetAll() =>
        Sources.Values.OrderBy(x => x.Name).ToList();

    public static CustomReportSourceDto GetRequired(string sourceKey) =>
        Sources.TryGetValue(sourceKey, out var source)
            ? source
            : throw new InvalidOperationException("Unknown report source.");

    public static IReadOnlyList<CustomReportColumnDto> ValidateColumns(
        string sourceKey,
        IReadOnlyList<string> requestedColumns)
    {
        var source = GetRequired(sourceKey);
        if (requestedColumns.Count == 0)
        {
            throw new InvalidOperationException("Select at least one column.");
        }

        var allowed = source.Columns.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var result = new List<CustomReportColumnDto>();

        foreach (var key in requestedColumns)
        {
            if (!allowed.TryGetValue(key, out var column))
            {
                throw new InvalidOperationException($"Column '{key}' is not allowed for {source.Name}.");
            }

            if (result.All(x => !x.Key.Equals(column.Key, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(column);
            }
        }

        return result;
    }

    private static IEnumerable<CustomReportSourceDto> BuildSources()
    {
        yield return Source(
            "customers",
            "Customers",
            "Customer master list and opening balances",
            false,
            null,
            Col("buyerId", "Buyer ID", "string"),
            Col("buyerName", "Buyer Name", "string"),
            Col("phone", "Phone", "string"),
            Col("mobile", "Mobile", "string"),
            Col("email", "Email", "string"),
            Col("ntn", "NTN", "string"),
            Col("strn", "STRN", "string"),
            Col("cnic", "CNIC", "string"),
            Col("openingBalance", "Opening Balance", "decimal"),
            Col("customerType", "Customer Type", "string"),
            Col("province", "Province", "string"),
            Col("scenario", "FBR Scenario", "string"),
            Col("address", "Address", "string"),
            Col("isActive", "Active", "boolean"),
            Col("createdAt", "Created At", "datetime"));

        yield return Source(
            "vendors",
            "Vendors",
            "Vendor master list and opening balances",
            false,
            null,
            Col("vendorCode", "Vendor Code", "string"),
            Col("vendorName", "Vendor Name", "string"),
            Col("phone", "Phone", "string"),
            Col("email", "Email", "string"),
            Col("ntn", "NTN", "string"),
            Col("openingBalance", "Opening Balance", "decimal"),
            Col("defaultSalesTaxRate", "Default Tax %", "decimal"),
            Col("address", "Address", "string"),
            Col("isActive", "Active", "boolean"),
            Col("createdAt", "Created At", "datetime"));

        yield return Source(
            "items",
            "Items",
            "Inventory and service items",
            false,
            null,
            Col("itemCode", "Item Code", "string"),
            Col("itemName", "Item Name", "string"),
            Col("itemType", "Item Type", "string"),
            Col("category", "Category", "string"),
            Col("unit", "Unit", "string"),
            Col("hsCode", "HS Code", "string"),
            Col("stackNo", "Stack No", "string"),
            Col("lotNo", "Lot No", "string"),
            Col("purchaseRate", "Purchase Rate", "decimal"),
            Col("saleRate", "Sale Rate", "decimal"),
            Col("currentStock", "Current Stock", "decimal"),
            Col("minimumStock", "Minimum Stock", "decimal"),
            Col("reorderLevel", "Reorder Level", "decimal"),
            Col("isActive", "Active", "boolean"),
            Col("createdAt", "Created At", "datetime"));

        yield return Source(
            "chart_of_accounts",
            "Chart of Accounts",
            "General ledger accounts",
            false,
            null,
            Col("accountNumber", "Account #", "string"),
            Col("accountName", "Account Name", "string"),
            Col("accountType", "Account Type", "string"),
            Col("subAccountType", "Sub Type", "string"),
            Col("parentAccount", "Parent Account", "string"),
            Col("openingBalance", "Opening Balance", "decimal"),
            Col("description", "Description", "string"),
            Col("isActive", "Active", "boolean"),
            Col("createdAt", "Created At", "datetime"));

        yield return Source(
            "sales_invoices",
            "Sales Invoices",
            "Sales invoice headers",
            true,
            "Invoice Date",
            Col("invoiceNumber", "Invoice #", "string"),
            Col("invoiceDate", "Invoice Date", "date"),
            Col("customerName", "Customer", "string"),
            Col("buyerId", "Buyer ID", "string"),
            Col("status", "Status", "string"),
            Col("invoiceType", "Invoice Type", "string"),
            Col("subTotal", "Subtotal", "decimal"),
            Col("discountAmount", "Discount", "decimal"),
            Col("taxAmount", "Tax", "decimal"),
            Col("netTotal", "Net Total", "decimal"),
            Col("fbrInvoiceNumber", "FBR #", "string"),
            Col("buyerNtn", "Buyer NTN", "string"),
            Col("createdAt", "Created At", "datetime"));

        yield return Source(
            "sales_invoice_lines",
            "Sales Invoice Lines",
            "Line-level sales detail",
            true,
            "Invoice Date",
            Col("invoiceNumber", "Invoice #", "string"),
            Col("invoiceDate", "Invoice Date", "date"),
            Col("customerName", "Customer", "string"),
            Col("itemCode", "Item Code", "string"),
            Col("itemName", "Item Name", "string"),
            Col("description", "Description", "string"),
            Col("quantity", "Quantity", "decimal"),
            Col("cartons", "Cartons", "decimal"),
            Col("price", "Price", "decimal"),
            Col("taxRate", "Tax Rate", "decimal"),
            Col("taxAmount", "Tax Amount", "decimal"),
            Col("discount", "Discount", "decimal"),
            Col("lineTotal", "Line Total", "decimal"),
            Col("stackNo", "Stack No", "string"),
            Col("lotNo", "Lot No", "string"));

        yield return Source(
            "vendor_bills",
            "Vendor Bills",
            "Purchase bill headers",
            true,
            "Bill Date",
            Col("billNumber", "Bill #", "string"),
            Col("billDate", "Bill Date", "date"),
            Col("vendorName", "Vendor", "string"),
            Col("vendorCode", "Vendor Code", "string"),
            Col("refNo", "Reference", "string"),
            Col("status", "Status", "string"),
            Col("totalQuantity", "Total Qty", "decimal"),
            Col("taxAmount", "Tax", "decimal"),
            Col("netAmount", "Net Amount", "decimal"),
            Col("createdAt", "Created At", "datetime"));

        yield return Source(
            "vendor_bill_lines",
            "Vendor Bill Lines",
            "Line-level purchase detail",
            true,
            "Bill Date",
            Col("billNumber", "Bill #", "string"),
            Col("billDate", "Bill Date", "date"),
            Col("vendorName", "Vendor", "string"),
            Col("itemCode", "Item Code", "string"),
            Col("description", "Description", "string"),
            Col("quantity", "Quantity", "decimal"),
            Col("cartons", "Cartons", "decimal"),
            Col("rate", "Rate", "decimal"),
            Col("amount", "Amount", "decimal"),
            Col("stackNo", "Stack No", "string"),
            Col("lotNo", "Lot No", "string"));

        yield return Source(
            "journal_entries",
            "Journal Entries",
            "Posted and draft journal headers",
            true,
            "Entry Date",
            Col("entryNumber", "Entry #", "string"),
            Col("entryDate", "Entry Date", "date"),
            Col("description", "Description", "string"),
            Col("status", "Status", "string"),
            Col("referenceType", "Reference Type", "string"),
            Col("referenceId", "Reference ID", "number"),
            Col("totalDebit", "Total Debit", "decimal"),
            Col("totalCredit", "Total Credit", "decimal"),
            Col("createdBy", "Created By", "string"),
            Col("createdAt", "Created At", "datetime"));

        yield return Source(
            "journal_entry_lines",
            "Journal Entry Lines",
            "Debit/credit lines by account",
            true,
            "Entry Date",
            Col("entryNumber", "Entry #", "string"),
            Col("entryDate", "Entry Date", "date"),
            Col("accountNumber", "Account #", "string"),
            Col("accountName", "Account Name", "string"),
            Col("debit", "Debit", "decimal"),
            Col("credit", "Credit", "decimal"),
            Col("memo", "Memo", "string"),
            Col("entryDescription", "Entry Description", "string"));

        yield return Source(
            "customer_receipts",
            "Customer Receipts",
            "Customer payment receipts",
            true,
            "Receipt Date",
            Col("receiptNumber", "Receipt #", "string"),
            Col("receiptDate", "Receipt Date", "date"),
            Col("customerName", "Customer", "string"),
            Col("amount", "Amount", "decimal"),
            Col("paymentMethod", "Payment Method", "string"),
            Col("bankName", "Bank", "string"),
            Col("chequeNumber", "Cheque #", "string"),
            Col("notes", "Notes", "string"),
            Col("createdAt", "Created At", "datetime"));

        yield return Source(
            "vendor_payments",
            "Vendor Payments",
            "Vendor payment records",
            true,
            "Payment Date",
            Col("paymentNumber", "Payment #", "string"),
            Col("paymentDate", "Payment Date", "date"),
            Col("vendorName", "Vendor", "string"),
            Col("amount", "Amount", "decimal"),
            Col("paymentMethod", "Payment Method", "string"),
            Col("bankName", "Bank", "string"),
            Col("chequeNumber", "Cheque #", "string"),
            Col("notes", "Notes", "string"),
            Col("createdAt", "Created At", "datetime"));

        yield return Source(
            "inventory_transactions",
            "Inventory Transactions",
            "Stock in/out/adjustment movements",
            true,
            "Transaction Date",
            Col("reference", "Reference", "string"),
            Col("transactionDate", "Date", "date"),
            Col("transactionType", "Type", "string"),
            Col("warehouse", "Warehouse", "string"),
            Col("itemCode", "Item Code", "string"),
            Col("itemName", "Item Name", "string"),
            Col("quantity", "Quantity", "decimal"),
            Col("rate", "Rate", "decimal"),
            Col("amount", "Amount", "decimal"),
            Col("stackNo", "Stack No", "string"),
            Col("lotNo", "Lot No", "string"),
            Col("notes", "Notes", "string"));

        yield return Source(
            "banks",
            "Bank Accounts",
            "Company bank accounts",
            false,
            null,
            Col("bankName", "Bank Name", "string"),
            Col("accountTitle", "Account Title", "string"),
            Col("accountNumber", "Account Number", "string"),
            Col("iban", "IBAN", "string"),
            Col("chartAccount", "GL Account", "string"),
            Col("openingBalance", "Opening Balance", "decimal"),
            Col("currentBalance", "Current Balance", "decimal"),
            Col("isActive", "Active", "boolean"),
            Col("createdAt", "Created At", "datetime"));
    }

    private static CustomReportSourceDto Source(
        string key,
        string name,
        string description,
        bool supportsDateFilter,
        string? dateColumnLabel,
        params CustomReportColumnDto[] columns) =>
        new(key, name, description, supportsDateFilter, dateColumnLabel, columns);

    private static CustomReportColumnDto Col(string key, string label, string dataType) =>
        new(key, label, dataType);
}
