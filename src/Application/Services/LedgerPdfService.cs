using System.Globalization;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PakistanAccountingERP.Application.Services;

public class LedgerPdfService : ILedgerPdfService
{
    private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-PK");

    static LedgerPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        _ = UrduPdfFont.Family;
    }

    public byte[] GeneratePdf(PartyLedgerPdfDto model)
    {
        var labels = LedgerPdfLabels.For(model.UseUrdu);
        var fontFamily = model.UseUrdu ? UrduPdfFont.Family : "Arial";

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily(fontFamily));

                page.Content().Column(column =>
                {
                    column.Item().AlignCenter().Text(model.Title).Bold().FontSize(14);
                    column.Item().PaddingTop(4).AlignCenter().Text(model.CompanyName).SemiBold().FontSize(10);
                    column.Item().PaddingTop(6).Text($"{model.PartyName} ({model.PartyCode})").Bold();
                    if (!string.IsNullOrWhiteSpace(model.PartyNtn))
                    {
                        column.Item().Text($"{labels.Ntn}: {model.PartyNtn}");
                    }

                    if (!string.IsNullOrWhiteSpace(model.PeriodLabel))
                    {
                        column.Item().PaddingTop(2).Text(model.PeriodLabel);
                    }

                    column.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text($"{labels.Opening}: {FormatAmount(model.OpeningBalance)}");
                        row.RelativeItem().AlignRight()
                            .Text($"{labels.Closing}: {FormatAmount(model.ClosingBalance)}").Bold();
                    });

                    column.Item().PaddingTop(8).Element(c => ComposeTable(c, model, labels));
                    column.Item().PaddingTop(8).AlignRight()
                        .Text($"{labels.Printed} {DateTime.Now:dd/MM/yyyy HH:mm}")
                        .FontSize(7).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeTable(IContainer container, PartyLedgerPdfDto model, LedgerPdfLabels labels)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(62);
                columns.RelativeColumn(1.2f);
                columns.RelativeColumn(2.2f);
                columns.RelativeColumn(1f);
                columns.RelativeColumn(1f);
                if (model.ShowPendingColumn)
                {
                    columns.RelativeColumn(1f);
                }
                columns.RelativeColumn(1.1f);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text(labels.Date);
                header.Cell().Element(HeaderCell).Text(labels.Reference);
                header.Cell().Element(HeaderCell).Text(labels.Description);
                header.Cell().Element(HeaderCell).AlignRight().Text(labels.Debit);
                header.Cell().Element(HeaderCell).AlignRight().Text(labels.Credit);
                if (model.ShowPendingColumn)
                {
                    header.Cell().Element(HeaderCell).AlignRight().Text(labels.Pending);
                }
                header.Cell().Element(HeaderCell).AlignRight().Text(labels.Balance);
            });

            foreach (var line in model.Lines)
            {
                var dateText = line.Date == DateTime.MinValue ? "—" : line.Date.ToString("dd/MM/yyyy");
                table.Cell().Element(BodyCell).Text(dateText);
                table.Cell().Element(BodyCell).Text(line.Reference);
                table.Cell().Element(BodyCell).Text(line.Description);
                table.Cell().Element(BodyCell).AlignRight().Text(line.Debit > 0 ? FormatAmount(line.Debit) : "—");
                table.Cell().Element(BodyCell).AlignRight().Text(line.Credit > 0 ? FormatAmount(line.Credit) : "—");
                if (model.ShowPendingColumn)
                {
                    table.Cell().Element(BodyCell).AlignRight()
                        .Text(line.PendingCredit > 0 ? FormatAmount(line.PendingCredit) : "—");
                }
                table.Cell().Element(BodyCell).AlignRight().Text(FormatAmount(line.Balance)).SemiBold();
            }
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.DefaultTextStyle(x => x.SemiBold())
            .BorderBottom(1).BorderColor(Colors.Grey.Medium)
            .PaddingVertical(4).PaddingHorizontal(3);

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(3).PaddingHorizontal(3);

    private static string FormatAmount(decimal value) =>
        value.ToString("N2", NumberCulture);
}
