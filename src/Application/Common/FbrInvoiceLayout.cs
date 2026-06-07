using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Common;

public static class FbrInvoiceLayout
{
    public const string PdfFooterNotice =
        "This is a system generated invoice and does not require any signatures.";

    public const string ScreenFooterNotice =
        "THIS IS SYSTEM GENERATED INVOICE DOES NOT REQUIRE SIGNATURE AND COMPANY STAMP.";

    public static string MapInvoiceTypeLabel(InvoiceType invoiceType) =>
        invoiceType switch
        {
            InvoiceType.DebitNote => "Debit Note",
            InvoiceType.CreditNote => "Credit Note",
            _ => "Sale Invoice"
        };

    public static string FormatTaxPeriod(DateTime invoiceDate) =>
        invoiceDate.ToString("yyyyMM");

    public static string FormatTaxRate(decimal taxRate) =>
        taxRate % 1 == 0 ? $"{(int)taxRate}%" : $"{taxRate:0.##}%";

    public static string ResolveNtnCnic(string? ntn, string? cnic)
    {
        if (!string.IsNullOrWhiteSpace(ntn))
        {
            return ntn.Trim();
        }

        return !string.IsNullOrWhiteSpace(cnic) ? cnic.Trim() : "—";
    }

    public static string BuildFbrProductDescription(
        string? itemDescription,
        string? lotNo,
        string? stackNo)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(itemDescription))
        {
            parts.Add(itemDescription.Trim());
        }

        if (!string.IsNullOrWhiteSpace(lotNo))
        {
            parts.Add($"Lot:{lotNo.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(stackNo))
        {
            parts.Add($"Stack:{stackNo.Trim()}");
        }

        return string.Join(" ", parts);
    }
}
