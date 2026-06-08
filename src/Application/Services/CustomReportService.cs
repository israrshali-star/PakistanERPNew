using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Application.Reports;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class CustomReportService : ICustomReportService
{
    private const int MaxAllowedRows = 10000;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;

    public CustomReportService(IUnitOfWork unitOfWork, ICurrentCompanyService currentCompany)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
    }

    public Task<IReadOnlyList<CustomReportSourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CustomReportSourceDto>>(CustomReportCatalog.GetAll());

    public async Task<CustomReportRunResult> RunAsync(
        CustomReportRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var source = CustomReportCatalog.GetRequired(request.SourceKey);
        var columns = CustomReportCatalog.ValidateColumns(request.SourceKey, request.Columns);
        var maxRows = Math.Clamp(request.MaxRows, 1, MaxAllowedRows);

        ValidateDateRange(source, request.FromDate, request.ToDate);

        var (rows, total) = await QueryAsync(
            request.SourceKey,
            companyId,
            request.FromDate,
            request.ToDate,
            maxRows,
            cancellationToken);

        var projected = rows
            .Select(row => ProjectRow(row, columns))
            .ToList();

        return new CustomReportRunResult(
            source.Name,
            columns,
            projected,
            total,
            total > maxRows);
    }

    public async Task<byte[]> ExportToExcelAsync(
        CustomReportRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(new CustomReportRunRequest
        {
            SourceKey = request.SourceKey,
            Columns = request.Columns,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            MaxRows = MaxAllowedRows
        }, cancellationToken);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(result.SourceName.Length > 31
            ? result.SourceName[..31]
            : result.SourceName);

        for (var c = 0; c < result.Columns.Count; c++)
        {
            worksheet.Cell(1, c + 1).Value = result.Columns[c].Label;
            worksheet.Cell(1, c + 1).Style.Font.Bold = true;
        }

        for (var r = 0; r < result.Rows.Count; r++)
        {
            for (var c = 0; c < result.Columns.Count; c++)
            {
                var key = result.Columns[c].Key;
                var value = result.Rows[r].GetValueOrDefault(key);
                SetCellValue(worksheet.Cell(r + 2, c + 1), value, result.Columns[c].DataType);
            }
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void ValidateDateRange(
        CustomReportSourceDto source,
        DateTime? fromDate,
        DateTime? toDate)
    {
        if (!source.SupportsDateFilter && (fromDate.HasValue || toDate.HasValue))
        {
            throw new InvalidOperationException($"{source.Name} does not support date filters.");
        }

        if (fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
        {
            throw new InvalidOperationException("From date cannot be after to date.");
        }
    }

    private async Task<(List<Dictionary<string, object?>> Rows, int Total)> QueryAsync(
        string sourceKey,
        int companyId,
        DateTime? fromDate,
        DateTime? toDate,
        int maxRows,
        CancellationToken cancellationToken) =>
        sourceKey.ToLowerInvariant() switch
        {
            "customers" => await QueryCustomersAsync(companyId, maxRows, cancellationToken),
            "vendors" => await QueryVendorsAsync(companyId, maxRows, cancellationToken),
            "items" => await QueryItemsAsync(companyId, maxRows, cancellationToken),
            "chart_of_accounts" => await QueryChartOfAccountsAsync(companyId, maxRows, cancellationToken),
            "sales_invoices" => await QuerySalesInvoicesAsync(companyId, fromDate, toDate, maxRows, cancellationToken),
            "sales_invoice_lines" => await QuerySalesInvoiceLinesAsync(companyId, fromDate, toDate, maxRows, cancellationToken),
            "vendor_bills" => await QueryVendorBillsAsync(companyId, fromDate, toDate, maxRows, cancellationToken),
            "vendor_bill_lines" => await QueryVendorBillLinesAsync(companyId, fromDate, toDate, maxRows, cancellationToken),
            "journal_entries" => await QueryJournalEntriesAsync(companyId, fromDate, toDate, maxRows, cancellationToken),
            "journal_entry_lines" => await QueryJournalEntryLinesAsync(companyId, fromDate, toDate, maxRows, cancellationToken),
            "customer_receipts" => await QueryCustomerReceiptsAsync(companyId, fromDate, toDate, maxRows, cancellationToken),
            "vendor_payments" => await QueryVendorPaymentsAsync(companyId, fromDate, toDate, maxRows, cancellationToken),
            "inventory_transactions" => await QueryInventoryTransactionsAsync(companyId, fromDate, toDate, maxRows, cancellationToken),
            "banks" => await QueryBanksAsync(companyId, maxRows, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported report source.")
        };

    private static Dictionary<string, object?> ProjectRow(
        Dictionary<string, object?> row,
        IReadOnlyList<CustomReportColumnDto> columns)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            row.TryGetValue(column.Key, out var value);
            result[column.Key] = value;
        }

        return result;
    }

    private static void SetCellValue(IXLCell cell, object? value, string dataType)
    {
        if (value is null)
        {
            cell.Value = string.Empty;
            return;
        }

        switch (dataType)
        {
            case "decimal" or "number" when value is decimal d:
                cell.Value = d;
                cell.Style.NumberFormat.Format = "#,##0.00";
                return;
            case "decimal" or "number":
                cell.Value = Convert.ToDecimal(value);
                cell.Style.NumberFormat.Format = "#,##0.00";
                return;
            case "date" or "datetime" when value is DateTime dt:
                cell.Value = dt;
                cell.Style.DateFormat.Format = dataType == "date" ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm";
                return;
            case "boolean" when value is bool b:
                cell.Value = b ? "Yes" : "No";
                return;
            default:
                cell.Value = value.ToString() ?? string.Empty;
                break;
        }
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QueryCustomersAsync(
        int companyId, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.Customer>()
            .Query()
            .Where(x => x.CompanyId == companyId);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.BuyerId)
            .Take(maxRows)
            .Select(x => new
            {
                x.BuyerId,
                x.BuyerName,
                x.Phone,
                x.Mobile,
                x.Email,
                x.NTN,
                x.STRN,
                x.CNIC,
                x.OpeningBalance,
                CustomerType = x.CustomerType.ToString(),
                Province = x.Province != null ? x.Province.Name : null,
                Scenario = x.ScenarioType.Description,
                x.Address,
                x.IsActive,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(x => Row(
            ("buyerId", x.BuyerId),
            ("buyerName", x.BuyerName),
            ("phone", x.Phone),
            ("mobile", x.Mobile),
            ("email", x.Email),
            ("ntn", x.NTN),
            ("strn", x.STRN),
            ("cnic", x.CNIC),
            ("openingBalance", x.OpeningBalance),
            ("customerType", x.CustomerType),
            ("province", x.Province),
            ("scenario", x.Scenario),
            ("address", x.Address),
            ("isActive", x.IsActive),
            ("createdAt", x.CreatedAt))).ToList(), total);
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QueryVendorsAsync(
        int companyId, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.Vendor>()
            .Query()
            .Where(x => x.CompanyId == companyId);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.VendorCode)
            .Take(maxRows)
            .Select(x => new
            {
                x.VendorCode,
                x.VendorName,
                x.Phone,
                x.Email,
                x.NTN,
                x.OpeningBalance,
                x.DefaultSalesTaxRate,
                x.Address,
                x.IsActive,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(x => Row(
            ("vendorCode", x.VendorCode),
            ("vendorName", x.VendorName),
            ("phone", x.Phone),
            ("email", x.Email),
            ("ntn", x.NTN),
            ("openingBalance", x.OpeningBalance),
            ("defaultSalesTaxRate", x.DefaultSalesTaxRate),
            ("address", x.Address),
            ("isActive", x.IsActive),
            ("createdAt", x.CreatedAt))).ToList(), total);
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QueryItemsAsync(
        int companyId, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.Item>()
            .Query()
            .Where(x => x.CompanyId == companyId);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.ItemCode)
            .Take(maxRows)
            .Select(x => new
            {
                x.ItemCode,
                x.ItemName,
                ItemType = x.ItemType.ToString(),
                Category = x.ItemCategory != null ? x.ItemCategory.Name : null,
                Unit = x.UnitOfMeasure.Symbol ?? x.UnitOfMeasure.Name,
                x.HSCode,
                x.StackNo,
                x.LotNo,
                x.PurchaseRate,
                x.SaleRate,
                x.CurrentStock,
                x.MinimumStock,
                x.ReorderLevel,
                x.IsActive,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(x => Row(
            ("itemCode", x.ItemCode),
            ("itemName", x.ItemName),
            ("itemType", x.ItemType),
            ("category", x.Category),
            ("unit", x.Unit),
            ("hsCode", x.HSCode),
            ("stackNo", x.StackNo),
            ("lotNo", x.LotNo),
            ("purchaseRate", x.PurchaseRate),
            ("saleRate", x.SaleRate),
            ("currentStock", x.CurrentStock),
            ("minimumStock", x.MinimumStock),
            ("reorderLevel", x.ReorderLevel),
            ("isActive", x.IsActive),
            ("createdAt", x.CreatedAt))).ToList(), total);
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QueryChartOfAccountsAsync(
        int companyId, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.ChartOfAccount>()
            .Query()
            .Where(x => x.CompanyId == companyId);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.AccountNumber)
            .Take(maxRows)
            .Select(x => new
            {
                x.AccountNumber,
                x.AccountName,
                AccountType = x.AccountType != null ? x.AccountType.TypeName : null,
                SubAccountType = x.SubAccountType != null ? x.SubAccountType.SubTypeName : null,
                ParentAccount = x.ParentAccount != null ? x.ParentAccount.AccountName : null,
                x.OpeningBalance,
                x.Description,
                x.IsActive,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(x => Row(
            ("accountNumber", x.AccountNumber),
            ("accountName", x.AccountName),
            ("accountType", x.AccountType),
            ("subAccountType", x.SubAccountType),
            ("parentAccount", x.ParentAccount),
            ("openingBalance", x.OpeningBalance),
            ("description", x.Description),
            ("isActive", x.IsActive),
            ("createdAt", x.CreatedAt))).ToList(), total);
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QuerySalesInvoicesAsync(
        int companyId, DateTime? fromDate, DateTime? toDate, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.SalesInvoice>()
            .Query()
            .Where(x => x.CompanyId == companyId);

        query = ApplyDateFilter(query, x => x.InvoiceDate, fromDate, toDate);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.Id)
            .Take(maxRows)
            .Select(x => new
            {
                x.InvoiceNumber,
                x.InvoiceDate,
                x.CustomerId,
                Status = x.Status.ToString(),
                InvoiceType = x.InvoiceType.ToString(),
                x.SubTotal,
                x.DiscountAmount,
                x.TaxAmount,
                x.NetTotal,
                x.FbrInvoiceNumber,
                x.BuyerNTN,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var customerIds = rows.Select(x => x.CustomerId).Distinct().ToList();
        var customers = await _unitOfWork.Repository<Domain.Entities.Customer>()
            .Query()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.BuyerName, c.BuyerId })
            .ToListAsync(cancellationToken);
        var customerLookup = customers.ToDictionary(c => c.Id);

        return (rows.Select(x =>
        {
            customerLookup.TryGetValue(x.CustomerId, out var customer);
            return Row(
            ("invoiceNumber", x.InvoiceNumber),
            ("invoiceDate", x.InvoiceDate),
            ("customerName", customer?.BuyerName),
            ("buyerId", customer?.BuyerId),
            ("status", x.Status),
            ("invoiceType", x.InvoiceType),
            ("subTotal", x.SubTotal),
            ("discountAmount", x.DiscountAmount),
            ("taxAmount", x.TaxAmount),
            ("netTotal", x.NetTotal),
            ("fbrInvoiceNumber", x.FbrInvoiceNumber),
            ("buyerNtn", x.BuyerNTN),
            ("createdAt", x.CreatedAt));
        }).ToList(), total);
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QuerySalesInvoiceLinesAsync(
        int companyId, DateTime? fromDate, DateTime? toDate, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.SalesInvoiceLine>()
            .Query()
            .Where(x => x.SalesInvoice.CompanyId == companyId);

        if (fromDate.HasValue)
        {
            query = query.Where(x => x.SalesInvoice.InvoiceDate >= fromDate.Value.Date);
        }

        if (toDate.HasValue)
        {
            var to = toDate.Value.Date.AddDays(1);
            query = query.Where(x => x.SalesInvoice.InvoiceDate < to);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.SalesInvoice.InvoiceDate)
            .ThenByDescending(x => x.Id)
            .Take(maxRows)
            .Select(x => new
            {
                x.SalesInvoice.InvoiceNumber,
                x.SalesInvoice.InvoiceDate,
                x.SalesInvoice.CustomerId,
                ItemCode = x.Item.ItemCode,
                ItemName = x.Item.ItemName,
                Description = x.ProductDescription,
                x.Quantity,
                x.Cartons,
                x.Price,
                x.TaxRate,
                x.TaxAmount,
                x.Discount,
                x.LineTotal,
                x.StackNo,
                x.LotNo
            })
            .ToListAsync(cancellationToken);

        var customerIds = rows.Select(x => x.CustomerId).Distinct().ToList();
        var customers = await _unitOfWork.Repository<Domain.Entities.Customer>()
            .Query()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.BuyerName })
            .ToListAsync(cancellationToken);
        var customerLookup = customers.ToDictionary(c => c.Id, c => c.BuyerName);

        return (rows.Select(x =>
        {
            customerLookup.TryGetValue(x.CustomerId, out var customerName);
            return Row(
                ("invoiceNumber", x.InvoiceNumber),
                ("invoiceDate", x.InvoiceDate),
                ("customerName", customerName),
                ("itemCode", x.ItemCode),
                ("itemName", x.ItemName),
                ("description", x.Description),
                ("quantity", x.Quantity),
                ("cartons", x.Cartons),
                ("price", x.Price),
                ("taxRate", x.TaxRate),
                ("taxAmount", x.TaxAmount),
                ("discount", x.Discount),
                ("lineTotal", x.LineTotal),
                ("stackNo", x.StackNo),
                ("lotNo", x.LotNo));
        }).ToList(), total);
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QueryVendorBillsAsync(
        int companyId, DateTime? fromDate, DateTime? toDate, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.VendorBill>()
            .Query()
            .Where(x => x.CompanyId == companyId);

        query = ApplyDateFilter(query, x => x.BillDate, fromDate, toDate);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.BillDate)
            .Take(maxRows)
            .Select(x => new
            {
                x.BillNumber,
                x.BillDate,
                VendorName = x.Vendor.VendorName,
                VendorCode = x.Vendor.VendorCode,
                x.RefNo,
                Status = x.Status.ToString(),
                x.TotalQuantity,
                x.TaxAmount,
                x.NetAmount,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(x => Row(
            ("billNumber", x.BillNumber),
            ("billDate", x.BillDate),
            ("vendorName", x.VendorName),
            ("vendorCode", x.VendorCode),
            ("refNo", x.RefNo),
            ("status", x.Status),
            ("totalQuantity", x.TotalQuantity),
            ("taxAmount", x.TaxAmount),
            ("netAmount", x.NetAmount),
            ("createdAt", x.CreatedAt))).ToList(), total);
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QueryVendorBillLinesAsync(
        int companyId, DateTime? fromDate, DateTime? toDate, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.VendorBillLine>()
            .Query()
            .Where(x => x.VendorBill.CompanyId == companyId);

        if (fromDate.HasValue)
        {
            query = query.Where(x => x.VendorBill.BillDate >= fromDate.Value.Date);
        }

        if (toDate.HasValue)
        {
            var to = toDate.Value.Date.AddDays(1);
            query = query.Where(x => x.VendorBill.BillDate < to);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.VendorBill.BillDate)
            .Take(maxRows)
            .Select(x => new
            {
                x.VendorBill.BillNumber,
                x.VendorBill.BillDate,
                VendorName = x.VendorBill.Vendor.VendorName,
                ItemCode = x.Item != null ? x.Item.ItemCode : null,
                x.Description,
                x.Quantity,
                x.Cartons,
                x.Rate,
                x.Amount,
                x.StackNo,
                x.LotNo
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(x => Row(
            ("billNumber", x.BillNumber),
            ("billDate", x.BillDate),
            ("vendorName", x.VendorName),
            ("itemCode", x.ItemCode),
            ("description", x.Description),
            ("quantity", x.Quantity),
            ("cartons", x.Cartons),
            ("rate", x.Rate),
            ("amount", x.Amount),
            ("stackNo", x.StackNo),
            ("lotNo", x.LotNo))).ToList(), total);
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QueryJournalEntriesAsync(
        int companyId, DateTime? fromDate, DateTime? toDate, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.JournalEntry>()
            .Query()
            .Where(x => x.CompanyId == companyId);

        query = ApplyDateFilter(query, x => x.EntryDate, fromDate, toDate);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.EntryDate)
            .Take(maxRows)
            .Select(x => new
            {
                x.EntryNumber,
                x.EntryDate,
                x.Description,
                Status = x.Status.ToString(),
                x.ReferenceType,
                x.ReferenceId,
                TotalDebit = x.Lines.Sum(l => l.Debit),
                TotalCredit = x.Lines.Sum(l => l.Credit),
                x.CreatedBy,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(x => Row(
            ("entryNumber", x.EntryNumber),
            ("entryDate", x.EntryDate),
            ("description", x.Description),
            ("status", x.Status),
            ("referenceType", x.ReferenceType),
            ("referenceId", x.ReferenceId),
            ("totalDebit", x.TotalDebit),
            ("totalCredit", x.TotalCredit),
            ("createdBy", x.CreatedBy),
            ("createdAt", x.CreatedAt))).ToList(), total);
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QueryJournalEntryLinesAsync(
        int companyId, DateTime? fromDate, DateTime? toDate, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.JournalEntryLine>()
            .Query()
            .Where(x => x.JournalEntry.CompanyId == companyId);

        if (fromDate.HasValue)
        {
            query = query.Where(x => x.JournalEntry.EntryDate >= fromDate.Value.Date);
        }

        if (toDate.HasValue)
        {
            var to = toDate.Value.Date.AddDays(1);
            query = query.Where(x => x.JournalEntry.EntryDate < to);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.JournalEntry.EntryDate)
            .Take(maxRows)
            .Select(x => new
            {
                x.JournalEntry.EntryNumber,
                x.JournalEntry.EntryDate,
                AccountNumber = x.ChartOfAccount.AccountNumber,
                AccountName = x.ChartOfAccount.AccountName,
                x.Debit,
                x.Credit,
                x.Memo,
                EntryDescription = x.JournalEntry.Description
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(x => Row(
            ("entryNumber", x.EntryNumber),
            ("entryDate", x.EntryDate),
            ("accountNumber", x.AccountNumber),
            ("accountName", x.AccountName),
            ("debit", x.Debit),
            ("credit", x.Credit),
            ("memo", x.Memo),
            ("entryDescription", x.EntryDescription))).ToList(), total);
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QueryCustomerReceiptsAsync(
        int companyId, DateTime? fromDate, DateTime? toDate, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.CustomerReceipt>()
            .Query()
            .Where(x => x.CompanyId == companyId);

        query = ApplyDateFilter(query, x => x.ReceiptDate, fromDate, toDate);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.ReceiptDate)
            .Take(maxRows)
            .Select(x => new
            {
                x.ReceiptNumber,
                x.ReceiptDate,
                CustomerName = x.Customer.BuyerName,
                x.Amount,
                PaymentMethod = x.PaymentMethod.ToString(),
                BankName = x.Bank != null ? x.Bank.BankName : null,
                x.ChequeNumber,
                x.Notes,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(x => Row(
            ("receiptNumber", x.ReceiptNumber),
            ("receiptDate", x.ReceiptDate),
            ("customerName", x.CustomerName),
            ("amount", x.Amount),
            ("paymentMethod", x.PaymentMethod),
            ("bankName", x.BankName),
            ("chequeNumber", x.ChequeNumber),
            ("notes", x.Notes),
            ("createdAt", x.CreatedAt))).ToList(), total);
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QueryVendorPaymentsAsync(
        int companyId, DateTime? fromDate, DateTime? toDate, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.VendorPayment>()
            .Query()
            .Where(x => x.CompanyId == companyId);

        query = ApplyDateFilter(query, x => x.PaymentDate, fromDate, toDate);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.PaymentDate)
            .Take(maxRows)
            .Select(x => new
            {
                x.PaymentNumber,
                x.PaymentDate,
                VendorName = x.Vendor.VendorName,
                x.Amount,
                PaymentMethod = x.PaymentMethod.ToString(),
                BankName = x.Bank != null ? x.Bank.BankName : null,
                x.ChequeNumber,
                x.Notes,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(x => Row(
            ("paymentNumber", x.PaymentNumber),
            ("paymentDate", x.PaymentDate),
            ("vendorName", x.VendorName),
            ("amount", x.Amount),
            ("paymentMethod", x.PaymentMethod),
            ("bankName", x.BankName),
            ("chequeNumber", x.ChequeNumber),
            ("notes", x.Notes),
            ("createdAt", x.CreatedAt))).ToList(), total);
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QueryInventoryTransactionsAsync(
        int companyId, DateTime? fromDate, DateTime? toDate, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.InventoryTransaction>()
            .Query()
            .Where(x => x.CompanyId == companyId);

        query = ApplyDateFilter(query, x => x.TransactionDate, fromDate, toDate);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.TransactionDate)
            .Take(maxRows)
            .Select(x => new
            {
                Reference = x.ReferenceNo,
                x.TransactionDate,
                TransactionType = x.TransactionType.ToString(),
                Warehouse = x.Warehouse.Name,
                ItemCode = x.Item.ItemCode,
                ItemName = x.Item.ItemName,
                x.Quantity,
                Rate = x.UnitCost,
                Amount = x.TotalCost,
                x.StackNo,
                x.LotNo,
                x.Notes
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(x => Row(
            ("reference", x.Reference),
            ("transactionDate", x.TransactionDate),
            ("transactionType", x.TransactionType),
            ("warehouse", x.Warehouse),
            ("itemCode", x.ItemCode),
            ("itemName", x.ItemName),
            ("quantity", x.Quantity),
            ("rate", x.Rate),
            ("amount", x.Amount),
            ("stackNo", x.StackNo),
            ("lotNo", x.LotNo),
            ("notes", x.Notes))).ToList(), total);
    }

    private async Task<(List<Dictionary<string, object?>>, int)> QueryBanksAsync(
        int companyId, int maxRows, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<Domain.Entities.Bank>()
            .Query()
            .Where(x => x.CompanyId == companyId);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.BankName)
            .Take(maxRows)
            .Select(x => new
            {
                x.BankName,
                x.AccountTitle,
                x.AccountNumber,
                x.IBAN,
                ChartAccount = x.ChartOfAccount != null
                    ? x.ChartOfAccount.AccountNumber + " · " + x.ChartOfAccount.AccountName
                    : null,
                x.OpeningBalance,
                x.CurrentBalance,
                x.IsActive,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(x => Row(
            ("bankName", x.BankName),
            ("accountTitle", x.AccountTitle),
            ("accountNumber", x.AccountNumber),
            ("iban", x.IBAN),
            ("chartAccount", x.ChartAccount),
            ("openingBalance", x.OpeningBalance),
            ("currentBalance", x.CurrentBalance),
            ("isActive", x.IsActive),
            ("createdAt", x.CreatedAt))).ToList(), total);
    }

    private static IQueryable<T> ApplyDateFilter<T>(
        IQueryable<T> query,
        System.Linq.Expressions.Expression<Func<T, DateTime>> dateSelector,
        DateTime? fromDate,
        DateTime? toDate)
    {
        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(BuildDateCompare(dateSelector, from, true));
        }

        if (toDate.HasValue)
        {
            var to = toDate.Value.Date.AddDays(1);
            query = query.Where(BuildDateCompare(dateSelector, to, false));
        }

        return query;
    }

    private static System.Linq.Expressions.Expression<Func<T, bool>> BuildDateCompare<T>(
        System.Linq.Expressions.Expression<Func<T, DateTime>> selector,
        DateTime value,
        bool isFrom)
    {
        var parameter = selector.Parameters[0];
        var property = selector.Body;
        var constant = System.Linq.Expressions.Expression.Constant(value);
        var comparison = isFrom
            ? System.Linq.Expressions.Expression.GreaterThanOrEqual(property, constant)
            : System.Linq.Expressions.Expression.LessThan(property, constant);
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(comparison, parameter);
    }

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] values)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            row[key] = value;
        }

        return row;
    }
}
