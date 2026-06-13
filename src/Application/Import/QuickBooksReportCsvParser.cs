using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace PakistanAccountingERP.Application.Import;

public sealed class QuickBooksNameBalanceRow
{
    public required string Name { get; init; }
    public decimal Balance { get; init; }
}

public sealed class QuickBooksOpenInvoiceRow
{
    public required string CustomerName { get; init; }
    public required string InvoiceNumber { get; init; }
    public DateTime InvoiceDate { get; init; }
    public decimal Amount { get; init; }
}

public sealed class QuickBooksOpenBillRow
{
    public required string VendorName { get; init; }
    public required string BillNumber { get; init; }
    public DateTime BillDate { get; init; }
    public decimal Amount { get; init; }
}

public sealed class QuickBooksInventoryValuationRow
{
    public required string ItemName { get; init; }
    public string? RawItemLabel { get; init; }
    public string? Description { get; init; }
    public string? UnitOfMeasure { get; init; }
    public decimal QuantityOnHand { get; init; }
    public decimal? AverageCost { get; init; }
    public decimal? AssetValue { get; init; }
}

public sealed class QuickBooksTrialBalanceCoaRow
{
    public required string ErpAccountNumber { get; init; }
    public decimal OpeningBalance { get; init; }
}

public sealed class OpeningStockStackLotRow
{
    public required string ItemCode { get; init; }
    public string? ItemName { get; init; }
    public string? StackNo { get; init; }
    public string? LotNo { get; init; }
    public string? Description { get; init; }
    public string? HsCode { get; init; }
    public string? Barcode { get; init; }
    public int? UnitOfMeasureId { get; init; }
    public decimal Cartons { get; init; }
    public decimal Weight { get; init; }
}

public static class QuickBooksReportCsvParser
{
    private static readonly Dictionary<string, string> QbAccountNumberToErp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["10800"] = "10015",
        ["10900"] = "10016",
        ["12000"] = "10017",
        ["15200"] = "15100",
        ["30800"] = "30020",
    };

    private static readonly HashSet<string> SkipErpAccountNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        "10000",
        "11000",
        "11110",
        "12100",
        "20000",
        "47900",
    };

    public static IReadOnlyList<QuickBooksTrialBalanceCoaRow> ParseTrialBalanceCoaOpenings(string filePath)
    {
        var rows = ReadReportRows(filePath);
        var openings = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (row.Count == 0)
            {
                continue;
            }

            var label = row[0].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(label)
                || label.StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase)
                || label.Equals("Debit", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var accountNumber = ExtractTrialBalanceAccountNumber(label);
            if (string.IsNullOrWhiteSpace(accountNumber))
            {
                continue;
            }

            if (QbAccountNumberToErp.TryGetValue(accountNumber, out var mapped))
            {
                accountNumber = mapped;
            }

            if (SkipErpAccountNumbers.Contains(accountNumber))
            {
                continue;
            }

            var debit = row.Count > 1 ? ParseDecimal(row[1]) : 0m;
            var credit = row.Count > 2 ? ParseDecimal(row[2]) : 0m;
            openings[accountNumber] = Math.Round(debit - credit, 2);
        }

        return openings
            .Select(kv => new QuickBooksTrialBalanceCoaRow
            {
                ErpAccountNumber = kv.Key,
                OpeningBalance = kv.Value
            })
            .ToList();
    }

    private static string? ExtractTrialBalanceAccountNumber(string label)
    {
        var subAccountMatch = Regex.Match(label, @":(\d{4,5})\b");
        if (subAccountMatch.Success)
        {
            return subAccountMatch.Groups[1].Value;
        }

        var topAccountMatch = Regex.Match(label, @"^\s*""?(\d{4,5})\b");
        return topAccountMatch.Success ? topAccountMatch.Groups[1].Value : null;
    }

    public static IReadOnlyList<QuickBooksNameBalanceRow> ParseNameBalanceReport(string filePath)
    {
        var rows = ReadReportRows(filePath);

        var detailHeaderIndex = FindDetailBalanceHeaderRowIndex(rows);
        if (detailHeaderIndex >= 0)
        {
            return ParseCustomerBalanceDetailRows(rows, detailHeaderIndex);
        }

        var headerIndex = FindHeaderRowIndex(rows, ["customer", "vendor"], ["balance", "amount", "total"]);
        if (headerIndex < 0)
        {
            var desktopSummary = TryParseQuickBooksDesktopSummaryRows(rows);
            if (desktopSummary.Count > 0)
            {
                return desktopSummary;
            }

            throw new InvalidOperationException(
                "Could not find customer/vendor balance columns. Expected columns like Customer and Balance in the Excel or CSV export from QuickBooks.");
        }

        var headers = rows[headerIndex];
        var nameIndex = FindColumnIndex(headers, ["customer", "vendor", "name"]);
        var balanceIndex = FindColumnIndex(headers, ["balance", "total balance", "amount", "open balance"]);

        if (nameIndex < 0 || balanceIndex < 0)
        {
            throw new InvalidOperationException("CSV is missing name or balance columns.");
        }

        var result = new List<QuickBooksNameBalanceRow>();
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (nameIndex >= row.Count || balanceIndex >= row.Count)
            {
                continue;
            }

            var name = row[nameIndex].Trim();
            if (string.IsNullOrWhiteSpace(name)
                || name.StartsWith("Total", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Grand", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var balance = ParseDecimal(row[balanceIndex]);
            if (balance == 0m)
            {
                continue;
            }

            result.Add(new QuickBooksNameBalanceRow { Name = name, Balance = balance });
        }

        return result;
    }

    /// <summary>
    /// QuickBooks Desktop CSV export: "Customer Name",balance per line with no header row.
    /// First row is often the report date; last row is TOTAL.
    /// </summary>
    private static List<QuickBooksNameBalanceRow> TryParseQuickBooksDesktopSummaryRows(
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var result = new List<QuickBooksNameBalanceRow>();

        foreach (var row in rows)
        {
            if (row.Count < 2)
            {
                continue;
            }

            var name = row[0].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(name)
                || name.StartsWith("Total", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Grand", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var balance = ParseDecimal(row[1]);
            if (balance == 0m)
            {
                continue;
            }

            result.Add(new QuickBooksNameBalanceRow { Name = name, Balance = balance });
        }

        return result;
    }

    private static int FindDetailBalanceHeaderRowIndex(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var normalized = rows[i].Select(NormalizeHeader).ToList();
            var hasType = normalized.Any(cell => cell == "type");
            var hasBalance = normalized.Any(cell => cell.Contains("balance", StringComparison.Ordinal));
            if (hasType && hasBalance)
            {
                return i;
            }
        }

        return -1;
    }

    private static IReadOnlyList<QuickBooksNameBalanceRow> ParseCustomerBalanceDetailRows(
        IReadOnlyList<IReadOnlyList<string>> rows,
        int headerIndex)
    {
        var headers = rows[headerIndex];
        var balanceIndex = FindColumnIndex(headers, ["balance"]);
        const int nameColumnIndex = 1;

        if (balanceIndex < 0)
        {
            throw new InvalidOperationException("Customer balance detail report is missing a Balance column.");
        }

        var result = new List<QuickBooksNameBalanceRow>();
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count <= nameColumnIndex)
            {
                continue;
            }

            var label = row[nameColumnIndex].Trim();
            if (!label.StartsWith("Total ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = label["Total ".Length..].Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var balance = ParseDecimal(GetCell(row, balanceIndex));
            result.Add(new QuickBooksNameBalanceRow { Name = name, Balance = balance });
        }

        return result;
    }

    public static IReadOnlyList<QuickBooksOpenInvoiceRow> ParseOpenInvoicesReport(string filePath)
    {
        var rows = ReadReportRows(filePath);
        var headerIndex = FindHeaderRowIndex(rows, ["customer", "name"], ["num", "invoice", "open"]);
        if (headerIndex < 0)
        {
            throw new InvalidOperationException(
                "Could not find open invoice columns in the CSV. Export the Open Invoices report from QuickBooks.");
        }

        var headers = rows[headerIndex];
        var customerIndex = FindColumnIndex(headers, ["customer", "name"]);
        var numberIndex = FindColumnIndex(headers, ["num", "invoice #", "invoice no", "number", "doc num"]);
        var dateIndex = FindColumnIndex(headers, ["date", "invoice date", "txn date"]);
        var amountIndex = FindColumnIndex(headers, ["open balance", "amount due", "balance", "amount"]);

        if (customerIndex < 0 || amountIndex < 0)
        {
            throw new InvalidOperationException("CSV is missing customer or amount columns for open invoices.");
        }

        var result = new List<QuickBooksOpenInvoiceRow>();
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var customer = GetCell(row, customerIndex);
            if (string.IsNullOrWhiteSpace(customer)
                || customer.StartsWith("Total", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var amount = ParseDecimal(GetCell(row, amountIndex));
            if (amount == 0m)
            {
                continue;
            }

            var invoiceNumber = GetCell(row, numberIndex);
            if (string.IsNullOrWhiteSpace(invoiceNumber))
            {
                invoiceNumber = $"QB-INV-{i}";
            }

            result.Add(new QuickBooksOpenInvoiceRow
            {
                CustomerName = customer,
                InvoiceNumber = invoiceNumber,
                InvoiceDate = ParseDate(GetCell(row, dateIndex)) ?? DateTime.UtcNow.Date,
                Amount = amount
            });
        }

        return result;
    }

    public static IReadOnlyList<QuickBooksOpenBillRow> ParseOpenBillsReport(string filePath)
    {
        var rows = ReadReportRows(filePath);
        var headerIndex = FindHeaderRowIndex(rows, ["vendor", "name"], ["num", "ref", "open", "due"]);
        if (headerIndex < 0)
        {
            throw new InvalidOperationException(
                "Could not find open bill columns in the CSV. Export the Unpaid Bills report from QuickBooks.");
        }

        var headers = rows[headerIndex];
        var vendorIndex = FindColumnIndex(headers, ["vendor", "name"]);
        var numberIndex = FindColumnIndex(headers, ["num", "ref no", "bill #", "number", "ref"]);
        var dateIndex = FindColumnIndex(headers, ["date", "bill date", "due date", "txn date"]);
        var amountIndex = FindColumnIndex(headers, ["open balance", "amount due", "balance", "amount"]);

        if (vendorIndex < 0 || amountIndex < 0)
        {
            throw new InvalidOperationException("CSV is missing vendor or amount columns for open bills.");
        }

        var result = new List<QuickBooksOpenBillRow>();
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var vendor = GetCell(row, vendorIndex);
            if (string.IsNullOrWhiteSpace(vendor)
                || vendor.StartsWith("Total", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var amount = ParseDecimal(GetCell(row, amountIndex));
            if (amount == 0m)
            {
                continue;
            }

            var billNumber = GetCell(row, numberIndex);
            if (string.IsNullOrWhiteSpace(billNumber))
            {
                billNumber = $"QB-BILL-{i}";
            }

            result.Add(new QuickBooksOpenBillRow
            {
                VendorName = vendor,
                BillNumber = billNumber,
                BillDate = ParseDate(GetCell(row, dateIndex)) ?? DateTime.UtcNow.Date,
                Amount = amount
            });
        }

        return result;
    }

    public static IReadOnlyList<QuickBooksInventoryValuationRow> ParseInventoryValuationReport(string filePath)
    {
        var rows = ReadReportRows(filePath);
        var (headerIndex, itemIndex, descriptionIndex, qtyIndex, uomIndex, avgCostIndex, assetValueIndex) =
            ResolveInventoryValuationColumns(rows);

        var result = new List<QuickBooksInventoryValuationRow>();
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var rawItemLabel = GetCell(row, itemIndex);
            var itemName = NormalizeInventoryItemKey(rawItemLabel);
            if (string.IsNullOrWhiteSpace(itemName)
                || itemName.StartsWith("Total", StringComparison.OrdinalIgnoreCase)
                || itemName.StartsWith("Grand", StringComparison.OrdinalIgnoreCase)
                || itemName.Equals("Inventory", StringComparison.OrdinalIgnoreCase)
                || itemName.Equals("Inventory Asset", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var quantity = ParseDecimal(GetCell(row, qtyIndex));
            var averageCost = avgCostIndex >= 0 ? ParseNullableDecimal(GetCell(row, avgCostIndex)) : null;
            var assetValue = assetValueIndex >= 0 ? ParseNullableDecimal(GetCell(row, assetValueIndex)) : null;

            if (quantity == 0m && (assetValue ?? 0m) == 0m)
            {
                continue;
            }

            result.Add(new QuickBooksInventoryValuationRow
            {
                ItemName = itemName,
                RawItemLabel = NullIfEmpty(rawItemLabel),
                Description = NullIfEmpty(GetCell(row, descriptionIndex)),
                UnitOfMeasure = uomIndex >= 0 ? NullIfEmpty(GetCell(row, uomIndex)) : null,
                QuantityOnHand = quantity,
                AverageCost = averageCost,
                AssetValue = assetValue
            });
        }

        return result;
    }

    public static bool IsOpeningStockStackLotFormat(string filePath)
    {
        var rows = ReadReportRows(filePath);
        if (rows.Count == 0)
        {
            return false;
        }

        var headers = rows[0].Select(NormalizeHeader).ToList();
        var hasItemCode = headers.Any(h => h.Contains("itemcode", StringComparison.Ordinal) || h == "item code");
        var hasStack = headers.Any(h => h.Contains("stack", StringComparison.Ordinal));
        var hasWeight = headers.Any(h => h.Contains("weight", StringComparison.Ordinal));
        return hasItemCode && hasStack && hasWeight;
    }

    public static IReadOnlyList<OpeningStockStackLotRow> ParseOpeningStockStackLotReport(string filePath)
    {
        var rows = ReadReportRows(filePath);
        if (rows.Count < 2)
        {
            throw new InvalidOperationException("Opening stock CSV is empty or missing data rows.");
        }

        var headers = rows[0].Select(NormalizeHeader).ToList();
        var itemCodeIndex = FindColumnIndex(headers, ["itemcode", "item code"]);
        var itemNameIndex = FindColumnIndex(headers, ["item name", "itemname", "name"]);
        var stackIndex = FindColumnIndex(headers, ["stack no", "stack no.", "stackno", "stack"]);
        var lotIndex = FindColumnIndex(headers, ["lot no", "lot no.", "lotno", "lot"]);
        var descriptionIndex = FindColumnIndex(headers, ["description", "discription", "desc"]);
        var hsCodeIndex = FindColumnIndex(headers, ["hscode", "hs code"]);
        var barcodeIndex = FindColumnIndex(headers, ["barcode"]);
        var uomIndex = FindColumnIndex(headers, ["unitof messure id", "unit of measure id", "unitofmeasureid", "uom id"]);
        var cartonsIndex = FindColumnIndex(headers, ["cartons", "carton", "ctn"]);
        var weightIndex = FindColumnIndex(headers, ["weight", "qty", "quantity"]);

        if (itemCodeIndex < 0 || weightIndex < 0)
        {
            throw new InvalidOperationException(
                "Opening stock CSV must include ItemCode and Weight columns.");
        }

        var result = new List<OpeningStockStackLotRow>();
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var itemCode = GetCell(row, itemCodeIndex).Trim();
            if (string.IsNullOrWhiteSpace(itemCode))
            {
                continue;
            }

            var weight = ParseDecimal(GetCell(row, weightIndex));
            var cartons = cartonsIndex >= 0 ? ParseDecimal(GetCell(row, cartonsIndex)) : 0m;
            if (weight == 0m && cartons == 0m)
            {
                continue;
            }

            int? unitId = null;
            if (uomIndex >= 0)
            {
                var uomText = GetCell(row, uomIndex).Trim();
                if (int.TryParse(uomText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedUom))
                {
                    unitId = parsedUom;
                }
            }

            result.Add(new OpeningStockStackLotRow
            {
                ItemCode = itemCode,
                ItemName = itemNameIndex >= 0 ? NullIfEmpty(GetCell(row, itemNameIndex)) : null,
                StackNo = stackIndex >= 0 ? NullIfEmpty(GetCell(row, stackIndex)) : null,
                LotNo = lotIndex >= 0 ? NullIfEmpty(GetCell(row, lotIndex)) : null,
                Description = descriptionIndex >= 0 ? NullIfEmpty(GetCell(row, descriptionIndex)) : null,
                HsCode = hsCodeIndex >= 0 ? NullIfEmpty(GetCell(row, hsCodeIndex)) : null,
                Barcode = barcodeIndex >= 0 ? NullIfEmpty(GetCell(row, barcodeIndex)) : null,
                UnitOfMeasureId = unitId,
                Cartons = cartons,
                Weight = weight
            });
        }

        return result;
    }

    public static string NormalizeInventoryItemKey(string raw)
    {
        var name = raw.Trim().Trim('"');
        var parenIndex = name.IndexOf('(');
        if (parenIndex > 0)
        {
            name = name[..parenIndex].Trim();
        }

        return name;
    }

    private static (int HeaderIndex, int ItemIndex, int DescriptionIndex, int QtyIndex, int UomIndex, int AvgCostIndex, int AssetValueIndex)
        ResolveInventoryValuationColumns(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var headerIndex = FindHeaderRowIndex(rows, ["item"], ["qty", "on hand", "quantity"]);
        if (headerIndex >= 0)
        {
            var headers = rows[headerIndex];
            var itemIndex = FindColumnIndex(headers, ["item", "inventory item", "sku"]);
            var qtyIndex = FindColumnIndex(headers, ["on hand", "qty on hand", "quantity", "qty"]);
            if (itemIndex >= 0 && qtyIndex >= 0)
            {
                return (
                    headerIndex,
                    itemIndex,
                    FindColumnIndex(headers, ["description", "sales desc"]),
                    qtyIndex,
                    FindColumnIndex(headers, ["u/m", "um", "unit", "unit of measure"]),
                    FindColumnIndex(headers, ["average cost", "avg cost", "cost"]),
                    FindColumnIndex(headers, ["asset value", "total value", "value"]));
            }
        }

        headerIndex = FindInventoryValuationHeaderRowIndex(rows);
        if (headerIndex < 0)
        {
            throw new InvalidOperationException(
                "Could not find inventory valuation columns. Expected QuickBooks Inventory Valuation Summary with On Hand and Avg Cost columns.");
        }

        var qbHeaders = rows[headerIndex];
        var onHandIndex = FindColumnIndex(qbHeaders, ["on hand", "qty on hand", "quantity", "qty"]);
        if (onHandIndex < 0)
        {
            throw new InvalidOperationException("Inventory valuation CSV is missing an On Hand quantity column.");
        }

        return (
            headerIndex,
            0,
            -1,
            onHandIndex,
            FindColumnIndex(qbHeaders, ["u/m", "um", "unit", "unit of measure"]),
            FindColumnIndex(qbHeaders, ["average cost", "avg cost", "cost"]),
            FindColumnIndex(qbHeaders, ["asset value", "total value", "value"]));
    }

    private static int FindInventoryValuationHeaderRowIndex(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var normalized = rows[i].Select(NormalizeHeader).ToList();
            var hasOnHand = normalized.Any(cell => cell.Contains("on hand", StringComparison.Ordinal)
                                                   || cell.Contains("qty on hand", StringComparison.Ordinal));
            var hasCost = normalized.Any(cell => cell.Contains("avg cost", StringComparison.Ordinal)
                                                 || cell.Contains("average cost", StringComparison.Ordinal)
                                                 || cell.Contains("asset value", StringComparison.Ordinal));
            if (hasOnHand && hasCost)
            {
                return i;
            }
        }

        return -1;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal? ParseNullableDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsed = ParseDecimal(value);
        return parsed == 0m ? null : parsed;
    }

    private static List<List<string>> ReadReportRows(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase)
            ? ReadExcelRows(filePath)
            : ReadCsvRows(filePath);
    }

    private static List<List<string>> ReadExcelRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets
            .OrderByDescending(ws => ws.LastRowUsed()?.RowNumber() ?? 0)
            .First();
        var usedRange = worksheet.RangeUsed();
        if (usedRange is null)
        {
            return [];
        }

        var rows = new List<List<string>>();
        foreach (var row in usedRange.Rows())
        {
            var cells = row.CellsUsed().ToList();
            if (cells.Count == 0)
            {
                continue;
            }

            var lastColumn = cells.Max(c => c.Address.ColumnNumber);
            var values = new string[lastColumn];
            foreach (var cell in cells)
            {
                values[cell.Address.ColumnNumber - 1] = GetExcelCellValue(cell);
            }

            for (var i = 0; i < values.Length; i++)
            {
                values[i] ??= string.Empty;
            }

            rows.Add(values.ToList());
        }

        return rows;
    }

    private static string GetExcelCellValue(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Number)
        {
            return cell.GetDouble().ToString(CultureInfo.InvariantCulture);
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            return cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return cell.GetFormattedString().Trim();
    }

    private static List<List<string>> ReadCsvRows(string filePath)
    {
        var rows = new List<List<string>>();
        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                continue;
            }

            rows.Add(ParseCsvLine(line));
        }

        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                cells.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        cells.Add(current.ToString());
        return cells;
    }

    private static int FindHeaderRowIndex(
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyList<string> nameKeywords,
        IReadOnlyList<string> valueKeywords)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var normalized = rows[i]
                .Select(NormalizeHeader)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (normalized.Count == 0)
            {
                continue;
            }

            var hasName = normalized.Any(cell => nameKeywords.Any(keyword => cell.Contains(keyword, StringComparison.Ordinal)));
            var hasValue = normalized.Any(cell => valueKeywords.Any(keyword => cell.Contains(keyword, StringComparison.Ordinal)));
            if (hasName && hasValue)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindColumnIndex(IReadOnlyList<string> headers, IReadOnlyList<string> candidates)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var header = NormalizeHeader(headers[i]);
            if (candidates.Any(candidate => header.Contains(candidate, StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return -1;
    }

    private static string NormalizeHeader(string value) =>
        value.Trim().Trim('"').ToLowerInvariant();

    private static string GetCell(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index].Trim().Trim('"') : string.Empty;

    private static decimal ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        var normalized = value.Trim().Trim('"')
            .Replace(",", string.Empty)
            .Replace("PKR", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(", "-", StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Trim();

        return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0m;
    }

    private static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = new[]
        {
            "MM/dd/yyyy",
            "M/d/yyyy",
            "dd/MM/yyyy",
            "d/M/yyyy",
            "yyyy-MM-dd"
        };

        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact.Date;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Date
            : null;
    }
}
