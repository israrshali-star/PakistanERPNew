using ClosedXML.Excel;

namespace PakistanAccountingERP.Application.Import;

public sealed record QuickBooksAccountsPayableBillRow(
    string RefNo,
    string VendorName,
    DateTime? BillDate,
    decimal NetAmount);

public sealed record QuickBooksAccountsPayableData(
    IReadOnlyList<QuickBooksAccountsPayableBillRow> Bills,
    decimal? ClosingBalance);

public static class QuickBooksAccountsPayableReader
{
    public static QuickBooksAccountsPayableData Read(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheets.FirstOrDefault(w =>
                         string.Equals(w.Name, "Sheet1", StringComparison.OrdinalIgnoreCase))
                     ?? workbook.Worksheets.First(w => w.RowsUsed().Count() > 1);

        var bills = new List<QuickBooksAccountsPayableBillRow>();
        decimal? closingBalance = null;

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var type = row.Cell(5).GetString().Trim();
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            if (string.Equals(type, "Bill", StringComparison.OrdinalIgnoreCase))
            {
                var refNo = row.Cell(9).GetString().Trim();
                var vendorName = row.Cell(11).GetString().Trim();
                if (string.IsNullOrWhiteSpace(refNo) || string.IsNullOrWhiteSpace(vendorName))
                {
                    continue;
                }

                var billDate = row.Cell(7).TryGetValue<DateTime>(out var parsedDate)
                    ? parsedDate.Date
                    : (DateTime?)null;
                var credit = ReadAmount(row.Cell(17));
                if (credit <= 0m)
                {
                    continue;
                }

                bills.Add(new QuickBooksAccountsPayableBillRow(
                    refNo,
                    vendorName,
                    billDate,
                    credit));
            }

            var balance = ReadNullableAmount(row.Cell(19));
            if (balance.HasValue)
            {
                closingBalance = balance.Value;
            }
        }

        return new QuickBooksAccountsPayableData(bills, closingBalance);
    }

    private static decimal ReadAmount(IXLCell cell)
    {
        if (cell.TryGetValue<decimal>(out var value))
        {
            return Math.Round(value, 2);
        }

        var text = cell.GetString().Replace(",", string.Empty).Trim();
        return decimal.TryParse(text, out var parsed) ? Math.Round(parsed, 2) : 0m;
    }

    private static decimal? ReadNullableAmount(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        return ReadAmount(cell);
    }
}
