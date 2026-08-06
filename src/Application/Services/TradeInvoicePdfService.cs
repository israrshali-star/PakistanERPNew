using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PakistanAccountingERP.Application.Services;

public class TradeInvoicePdfService : ITradeInvoicePdfService
{
    private static readonly Color BorderGray = Color.FromHex("#333333");

    static TradeInvoicePdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        _ = UrduPdfFont.Family;
    }

    public byte[] GeneratePdf(TradeInvoicePrintDto model)
    {
        var labels = TradeDocumentPdfLabels.For(model.UseUrdu);
        var fontFamily = model.UseUrdu ? UrduPdfFont.Family : "Arial";

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily(fontFamily));

                page.Content().Column(column =>
                {
                    column.Item().Element(c => ComposeHeader(c, model, labels));
                    column.Item().PaddingTop(10).Element(c => ComposeLinesTable(c, model, labels));
                    column.Item().PaddingTop(10).Element(c => ComposeFooter(c, model, labels));
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, TradeInvoicePrintDto model, TradeDocumentPdfLabels labels)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(model.SellerName.ToUpperInvariant()).Bold().FontSize(14)
                        .Underline();
                    left.Item().PaddingTop(8).Text(model.CustomerName).FontSize(10);
                });

                row.ConstantItem(130).Border(1).BorderColor(BorderGray).Padding(6).Column(right =>
                {
                    right.Item().Row(r =>
                    {
                        r.ConstantItem(52).Text(labels.Date).SemiBold();
                        r.RelativeItem().Text(model.InvoiceDate.ToString("dd/MM/yyyy"));
                    });
                    right.Item().PaddingTop(4).Row(r =>
                    {
                        r.ConstantItem(52).Text(labels.InvoiceNumber).SemiBold();
                        r.RelativeItem().Text(model.InvoiceNumber);
                    });
                });
            });
        });
    }

    private static void ComposeLinesTable(IContainer container, TradeInvoicePrintDto model, TradeDocumentPdfLabels labels)
    {
        var weightLines = model.Lines
            .Where(l => TradeInvoiceLayout.CountsTowardWeightAndCartonTotals(l.ItemType, l.ItemCode))
            .ToList();
        var totalCartons = weightLines.Sum(l => l.Cartons);
        var totalQty = weightLines.Sum(l => l.Quantity);
        var totalAmount = model.Lines.Sum(l => l.Amount);

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3);
                columns.RelativeColumn(2);
                columns.ConstantColumn(52);
                columns.ConstantColumn(52);
                columns.ConstantColumn(52);
                columns.ConstantColumn(62);
            });

            table.Header(header =>
            {
                HeaderCell(header.Cell(), labels.Description);
                HeaderCell(header.Cell(), labels.CartonDescription);
                HeaderCell(header.Cell(), labels.NoOfCtn);
                HeaderCell(header.Cell(), labels.Qty);
                HeaderCell(header.Cell(), labels.Rate);
                HeaderCell(header.Cell(), labels.Amount);
            });

            foreach (var line in model.Lines)
            {
                table.Cell().Element(BodyCell).Text(line.Description);
                table.Cell().Element(BodyCell).Text(line.CartonDescription ?? string.Empty);
                table.Cell().Element(BodyCell).AlignRight().Text(FormatQty(line.Cartons));
                table.Cell().Element(BodyCell).AlignRight().Text(FormatQty(line.Quantity));
                table.Cell().Element(BodyCell).AlignRight().Text(TradeInvoiceLayout.FormatAmount(line.Rate));
                table.Cell().Element(BodyCell).AlignRight().Text(TradeInvoiceLayout.FormatAmount(line.Amount));
            }

            table.Cell().ColumnSpan(2).Element(TotalCell).Text(labels.Total).Bold();
            table.Cell().Element(TotalCell).AlignRight().Text(FormatQty(totalCartons)).Bold();
            table.Cell().Element(TotalCell).AlignRight().Text(FormatQty(totalQty)).Bold();
            table.Cell().Element(TotalCell).Text(string.Empty);
            table.Cell().Element(TotalCell).AlignRight().Text(TradeInvoiceLayout.FormatAmount(totalAmount)).Bold();
        });
    }

    private static void ComposeFooter(IContainer container, TradeInvoicePrintDto model, TradeDocumentPdfLabels labels)
    {
        container.Row(row =>
        {
            row.RelativeItem().Border(1).BorderColor(BorderGray).Padding(8).Column(left =>
            {
                left.Item().Text(labels.CustomerTotalBalance).Bold();
                left.Item().PaddingTop(6)
                    .Text($"PKR {TradeInvoiceLayout.FormatAmount(model.CustomerTotalBalance)}")
                    .FontSize(11);
            });

            row.ConstantItem(180).Border(1).BorderColor(BorderGray).Padding(8).Column(right =>
            {
                right.Item().Row(r =>
                {
                    r.RelativeItem().Text($"{labels.SalesTax} ({TradeInvoiceLayout.FormatTaxRatePrecise(model.TaxRateDisplay)}%)");
                    r.ConstantItem(70).AlignRight().Text($"PKR {TradeInvoiceLayout.FormatAmount(model.TaxAmount)}");
                });
                right.Item().PaddingTop(8).Row(r =>
                {
                    r.RelativeItem().Text(labels.Total).Bold();
                    r.ConstantItem(70).AlignRight().Text($"PKR {TradeInvoiceLayout.FormatAmount(model.NetTotal)}").Bold();
                });
            });
        });
    }

    private static void HeaderCell(IContainer container, string text)
    {
        container.Border(1).BorderColor(BorderGray).Background(Colors.Grey.Lighten4).Padding(4)
            .Text(text).SemiBold().FontSize(8);
    }

    private static IContainer BodyCell(IContainer container) =>
        container.Border(1).BorderColor(BorderGray).Padding(4).DefaultTextStyle(x => x.FontSize(8));

    private static IContainer TotalCell(IContainer container) =>
        container.Border(1).BorderColor(BorderGray).Padding(4);

    private static string FormatQty(decimal value) =>
        value % 1 == 0 ? ((int)value).ToString() : value.ToString("N2", TradeInvoiceLayout.NumberCulture);
}
