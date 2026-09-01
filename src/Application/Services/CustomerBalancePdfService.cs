using System.Globalization;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PakistanAccountingERP.Application.Services;

public class CustomerBalancePdfService : ICustomerBalancePdfService
{
    private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-PK");

    static CustomerBalancePdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GeneratePdf(CustomerBalanceReportDto report, string companyName)
    {
        var period = $"Period: {report.FromDate:dd/MM/yyyy} to {report.ToDate:dd/MM/yyyy}" +
                     $" — {report.CustomerCount} customer(s) with balances";
        if (!string.IsNullOrWhiteSpace(report.CustomerLabel))
        {
            period += $" — {report.CustomerLabel}";
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
                    header.Item().AlignCenter().Text("Customer Balances").Bold().FontSize(14);
                    header.Item().PaddingTop(2).AlignCenter().Text(companyName).SemiBold().FontSize(10);
                    header.Item().PaddingTop(4).AlignCenter().Text(period).FontSize(8).FontColor(Colors.Grey.Darken1);
                    header.Item().PaddingTop(2).AlignCenter()
                        .Text("Balances of 1,000 or less are hidden.")
                        .FontSize(7).FontColor(Colors.Grey.Darken1);
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

    private static void ComposeTable(IContainer container, CustomerBalanceReportDto report)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(70);
                columns.RelativeColumn(2.4f);
                columns.ConstantColumn(78);
                columns.ConstantColumn(78);
                columns.ConstantColumn(78);
                columns.ConstantColumn(78);
                columns.ConstantColumn(82);
                columns.ConstantColumn(82);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Code");
                header.Cell().Element(HeaderCell).Text("Customer");
                header.Cell().Element(HeaderCell).AlignRight().Text("Opening Dr");
                header.Cell().Element(HeaderCell).AlignRight().Text("Opening Cr");
                header.Cell().Element(HeaderCell).AlignRight().Text("Period Dr");
                header.Cell().Element(HeaderCell).AlignRight().Text("Period Cr");
                header.Cell().Element(HeaderCell).AlignRight().Text("Closing Dr");
                header.Cell().Element(HeaderCell).AlignRight().Text("Closing Cr");
            });

            if (report.Lines.Count == 0)
            {
                table.Cell().ColumnSpan(8).Element(BodyCell)
                    .AlignCenter().Text("No customer balances found.").FontColor(Colors.Grey.Darken1);
                return;
            }

            foreach (var line in report.Lines)
            {
                table.Cell().Element(BodyCell).Text(line.CustomerCode);
                table.Cell().Element(BodyCell).Text(line.CustomerName);
                table.Cell().Element(BodyCell).AlignRight().Text(FormatAmount(line.OpeningDebit));
                table.Cell().Element(BodyCell).AlignRight().Text(FormatAmount(line.OpeningCredit));
                table.Cell().Element(BodyCell).AlignRight().Text(FormatAmount(line.PeriodDebit));
                table.Cell().Element(BodyCell).AlignRight().Text(FormatAmount(line.PeriodCredit));
                table.Cell().Element(BodyCell).AlignRight().Text(FormatAmount(line.ClosingDebit)).SemiBold();
                table.Cell().Element(BodyCell).AlignRight().Text(FormatAmount(line.ClosingCredit)).SemiBold();
            }

            table.Cell().ColumnSpan(2).Element(TotalCell).AlignRight().Text("Totals").Bold();
            table.Cell().Element(TotalCell).AlignRight().Text(FormatAmount(report.TotalOpeningDebit)).Bold();
            table.Cell().Element(TotalCell).AlignRight().Text(FormatAmount(report.TotalOpeningCredit)).Bold();
            table.Cell().Element(TotalCell).AlignRight().Text(FormatAmount(report.TotalPeriodDebit)).Bold();
            table.Cell().Element(TotalCell).AlignRight().Text(FormatAmount(report.TotalPeriodCredit)).Bold();
            table.Cell().Element(TotalCell).AlignRight().Text(FormatAmount(report.TotalClosingDebit)).Bold();
            table.Cell().Element(TotalCell).AlignRight().Text(FormatAmount(report.TotalClosingCredit)).Bold();
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.DefaultTextStyle(x => x.SemiBold())
            .BorderBottom(1).BorderColor(Colors.Grey.Medium)
            .PaddingVertical(3).PaddingHorizontal(3)
            .Background(Colors.Grey.Lighten3);

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(2).PaddingHorizontal(3);

    private static IContainer TotalCell(IContainer container) =>
        container.BorderTop(1).BorderColor(Colors.Grey.Medium)
            .PaddingVertical(4).PaddingHorizontal(3)
            .Background(Colors.Grey.Lighten3);

    private static string FormatAmount(decimal value) =>
        Math.Abs(value) < 0.005m ? string.Empty : value.ToString("N2", NumberCulture);
}
