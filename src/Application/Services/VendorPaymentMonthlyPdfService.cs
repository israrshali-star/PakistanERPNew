using System.Globalization;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PakistanAccountingERP.Application.Services;

public class VendorPaymentMonthlyPdfService : IVendorPaymentMonthlyPdfService
{
    private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-PK");

    static VendorPaymentMonthlyPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GeneratePdf(VendorPaymentMonthlyReportDto report, string companyName)
    {
        var period = $"Period: {report.FromDate:dd/MM/yyyy} to {report.ToDate:dd/MM/yyyy}" +
                     $" — {report.PaymentCount} payment(s)";
        if (!string.IsNullOrWhiteSpace(report.VendorLabel))
        {
            period += $" — {report.VendorLabel}";
        }

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(14);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Arial"));

                page.Header().Column(header =>
                {
                    header.Item().AlignCenter().Text("Vendor Payments (Monthly)").Bold().FontSize(14);
                    header.Item().PaddingTop(2).AlignCenter().Text(companyName).SemiBold().FontSize(10);
                    header.Item().PaddingTop(4).AlignCenter().Text(period).FontSize(8).FontColor(Colors.Grey.Darken1);
                    header.Item().PaddingTop(6);
                });

                page.Content().Element(c => ComposeTable(c, report));

                page.Footer().DefaultTextStyle(x => x.FontSize(7).FontColor(Colors.Grey.Darken1)).Row(row =>
                {
                    row.RelativeItem().Text($"Printed {DateTime.Now:dd/MM/yyyy HH:mm}");
                    row.RelativeItem().AlignRight().Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeTable(IContainer container, VendorPaymentMonthlyReportDto report)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(58);
                columns.ConstantColumn(72);
                columns.RelativeColumn(1.8f);
                columns.ConstantColumn(78);
                columns.ConstantColumn(70);
                columns.ConstantColumn(78);
                columns.RelativeColumn(1.6f);
                columns.ConstantColumn(72);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Date");
                header.Cell().Element(HeaderCell).Text("Payment #");
                header.Cell().Element(HeaderCell).Text("Vendor");
                header.Cell().Element(HeaderCell).Text("Type");
                header.Cell().Element(HeaderCell).Text("Method");
                header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
                header.Cell().Element(HeaderCell).Text("Ref #");
                header.Cell().Element(HeaderCell).AlignRight().Text("Applied");
            });

            if (report.Months.Count == 0)
            {
                table.Cell().ColumnSpan(8).Element(BodyCell)
                    .AlignCenter().Text("No vendor payments found.").FontColor(Colors.Grey.Darken1);
                return;
            }

            foreach (var month in report.Months)
            {
                table.Cell().ColumnSpan(5).Element(MonthCell)
                    .Text($"{month.MonthLabel} ({month.PaymentCount})").SemiBold();
                table.Cell().Element(MonthCell).AlignRight().Text(FormatAmount(month.TotalAmount)).SemiBold();
                table.Cell().ColumnSpan(2).Element(MonthCell).Text(string.Empty);

                foreach (var line in month.Lines)
                {
                    table.Cell().Element(BodyCell).Text(line.PaymentDate.ToString("dd/MM/yyyy"));
                    table.Cell().Element(BodyCell).Text(line.PaymentNumber);
                    table.Cell().Element(BodyCell).Text(line.VendorName);
                    table.Cell().Element(BodyCell).Text(line.Source);
                    table.Cell().Element(BodyCell).Text(line.PaymentMethod);
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatAmount(line.Amount)).SemiBold();
                    table.Cell().Element(BodyCell).Text(BuildRefText(line));
                    table.Cell().Element(BodyCell).AlignRight().Text(BuildAppliedText(line));
                }
            }

            table.Cell().ColumnSpan(5).Element(TotalCell).AlignRight().Text("Grand Total").Bold();
            table.Cell().Element(TotalCell).AlignRight().Text(FormatAmount(report.TotalAmount)).Bold();
            table.Cell().ColumnSpan(2).Element(TotalCell).Text(string.Empty);
        });
    }

    private static string BuildRefText(VendorPaymentMonthlyLineDto line)
    {
        var parts = line.AppliedRefs
            .Select(r =>
            {
                var date = r.BillDate.HasValue ? $" ({r.BillDate.Value:dd/MM})" : string.Empty;
                return $"{r.RefNo}{date}";
            })
            .ToList();

        if (line.UnallocatedAmount > 0.004m)
        {
            parts.Add("Advance / Unallocated");
        }

        return parts.Count == 0 ? "—" : string.Join("\n", parts);
    }

    private static string BuildAppliedText(VendorPaymentMonthlyLineDto line)
    {
        var parts = line.AppliedRefs.Select(r => FormatAmount(r.AppliedAmount)).ToList();
        if (line.UnallocatedAmount > 0.004m)
        {
            parts.Add(FormatAmount(line.UnallocatedAmount));
        }

        return parts.Count == 0 ? "—" : string.Join("\n", parts);
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.DefaultTextStyle(x => x.SemiBold())
            .BorderBottom(1).BorderColor(Colors.Grey.Medium)
            .PaddingVertical(3).PaddingHorizontal(3)
            .Background(Colors.Grey.Lighten3);

    private static IContainer MonthCell(IContainer container) =>
        container.DefaultTextStyle(x => x.FontSize(8))
            .Background(Colors.Grey.Lighten4)
            .BorderBottom(1).BorderColor(Colors.Grey.Lighten1)
            .PaddingVertical(3).PaddingHorizontal(3);

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(2).PaddingHorizontal(3);

    private static IContainer TotalCell(IContainer container) =>
        container.BorderTop(1).BorderColor(Colors.Grey.Medium)
            .PaddingVertical(4).PaddingHorizontal(3)
            .Background(Colors.Grey.Lighten3);

    private static string FormatAmount(decimal value) =>
        value.ToString("N2", NumberCulture);
}
