using ClosedXML.Excel;

namespace PakistanAccountingERP.Application.Import;

public sealed record QuickBooksSalesTaxPayableData(
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal FurtherTaxClosingBalance,
    decimal SalesTax18ClosingBalance);

public static class QuickBooksSalesTaxPayableReader
{
    public static QuickBooksSalesTaxPayableData Read(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheets.FirstOrDefault(w =>
                         string.Equals(w.Name, "Sheet1", StringComparison.OrdinalIgnoreCase))
                     ?? workbook.Worksheets.First(w => w.RowsUsed().Count() > 1);

        decimal? openingBalance = null;
        decimal? closingBalance = null;
        decimal furtherTaxCredits = 0m;
        decimal salesTax18Credits = 0m;

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var accountLabel = row.Cell(2).GetString().Trim();
            if (accountLabel.Contains("Sales Tax Payable", StringComparison.OrdinalIgnoreCase)
                && !accountLabel.StartsWith("Total", StringComparison.OrdinalIgnoreCase)
                && openingBalance is null)
            {
                openingBalance = ReadNullableAmount(row.Cell(21));
            }

            if (accountLabel.StartsWith("Total", StringComparison.OrdinalIgnoreCase)
                && accountLabel.Contains("Sales Tax Payable", StringComparison.OrdinalIgnoreCase))
            {
                closingBalance = ReadNullableAmount(row.Cell(21));
            }

            var type = row.Cell(5).GetString().Trim();
            if (!string.Equals(type, "Invoice", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var memo = row.Cell(13).GetString().Trim();
            var credit = ReadAmount(row.Cell(19));
            if (credit <= 0m)
            {
                continue;
            }

            if (memo.Contains("18%", StringComparison.OrdinalIgnoreCase))
            {
                salesTax18Credits += credit;
            }
            else if (memo.Contains("4%", StringComparison.OrdinalIgnoreCase)
                     || memo.Contains("2%", StringComparison.OrdinalIgnoreCase))
            {
                furtherTaxCredits += credit;
            }
        }

        if (!openingBalance.HasValue || !closingBalance.HasValue)
        {
            throw new InvalidOperationException("Could not read opening or closing balance from the sales tax payable export.");
        }

        var totalCredits = furtherTaxCredits + salesTax18Credits;
        if (totalCredits <= 0m)
        {
            throw new InvalidOperationException("Could not read invoice tax credits from the sales tax payable export.");
        }

        var furtherShare = furtherTaxCredits / totalCredits;
        var furtherClosing = Math.Round(closingBalance.Value * furtherShare, 2);
        var salesTax18Closing = Math.Round(closingBalance.Value - furtherClosing, 2);

        return new QuickBooksSalesTaxPayableData(
            openingBalance.Value,
            closingBalance.Value,
            furtherClosing,
            salesTax18Closing);
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
