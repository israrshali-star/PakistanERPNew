using ClosedXML.Excel;

namespace PakistanAccountingERP.Application.Import;

public sealed record QuickBooksInventoryBillAmounts(
    string QuickBooksNumber,
    decimal InventoryDebit,
    decimal InventoryCredit)
{
    public decimal InventoryNet => Math.Round(InventoryDebit - InventoryCredit, 2);
}

public sealed record QuickBooksInventoryInvoiceAmounts(
    string QuickBooksNumber,
    decimal CogsCredit);

public sealed record QuickBooksInventoryLedgerData(
    IReadOnlyList<QuickBooksInventoryBillAmounts> Bills,
    IReadOnlyList<QuickBooksInventoryInvoiceAmounts> Invoices,
    decimal TotalBillDebits,
    decimal TotalBillCredits,
    decimal TotalInvoiceCredits,
    decimal ClosingBalance);

public static class QuickBooksInventoryLedgerReader
{
    public static QuickBooksInventoryLedgerData Read(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheets.FirstOrDefault(w =>
                         string.Equals(w.Name, "Sheet1", StringComparison.OrdinalIgnoreCase))
                     ?? workbook.Worksheets.Last();

        var billTotals = new Dictionary<string, (decimal Debit, decimal Credit)>(StringComparer.OrdinalIgnoreCase);
        var invoiceTotals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        decimal? closingBalance = null;

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var label = row.Cell(1).GetString().Trim();
            if (label.Contains("Total 12110", StringComparison.OrdinalIgnoreCase)
                || label.Equals("TOTAL", StringComparison.OrdinalIgnoreCase))
            {
                closingBalance ??= row.Cell(22).TryGetValue<decimal>(out var balance)
                    ? Math.Round(balance, 2)
                    : null;
                continue;
            }

            var type = row.Cell(6).GetString().Trim();
            var number = row.Cell(10).GetString().Trim();
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(number))
            {
                continue;
            }

            var debit = ReadAmount(row.Cell(18));
            var credit = ReadAmount(row.Cell(20));

            if (string.Equals(type, "Bill", StringComparison.OrdinalIgnoreCase))
            {
                if (!billTotals.TryGetValue(number, out var totals))
                {
                    totals = (0m, 0m);
                }

                billTotals[number] = (
                    Math.Round(totals.Debit + debit, 2),
                    Math.Round(totals.Credit + credit, 2));
            }
            else if (string.Equals(type, "Invoice", StringComparison.OrdinalIgnoreCase))
            {
                invoiceTotals[number] = Math.Round(
                    invoiceTotals.GetValueOrDefault(number) + credit,
                    2);
            }
        }

        var bills = billTotals
            .OrderBy(b => int.TryParse(b.Key, out var n) ? n : int.MaxValue)
            .Select(b => new QuickBooksInventoryBillAmounts(b.Key, b.Value.Debit, b.Value.Credit))
            .ToList();

        var invoices = invoiceTotals
            .OrderBy(i => int.TryParse(i.Key, out var n) ? n : int.MaxValue)
            .Select(i => new QuickBooksInventoryInvoiceAmounts(i.Key, i.Value))
            .ToList();

        var totalBillDebits = bills.Sum(b => b.InventoryDebit);
        var totalBillCredits = bills.Sum(b => b.InventoryCredit);
        var totalInvoiceCredits = invoices.Sum(i => i.CogsCredit);

        if (!closingBalance.HasValue)
        {
            const decimal opening = 7_497_916.51m;
            closingBalance = Math.Round(
                opening + totalBillDebits - totalBillCredits - totalInvoiceCredits,
                2);
        }

        return new QuickBooksInventoryLedgerData(
            bills,
            invoices,
            totalBillDebits,
            totalBillCredits,
            totalInvoiceCredits,
            closingBalance.Value);
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
}
