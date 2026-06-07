using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace PakistanAccountingERP.Application.Services;

public class SalesInvoicePdfService : ISalesInvoicePdfService
{
    private static readonly Color GreenDark = Color.FromHex("#1F6B45");
    private static readonly Color GreenLight = Color.FromHex("#D9F0E3");
    private static readonly Color GrayBox = Color.FromHex("#F2F2F2");
    private static readonly Color BorderGray = Color.FromHex("#D0D0D0");
    private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-PK");

    static SalesInvoicePdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GeneratePdf(SalesInvoicePrintDto model) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Content().Column(column =>
                {
                    column.Item().Element(c => ComposeHeader(c, model));
                    column.Item().PaddingTop(10).Element(c => ComposePartyBoxes(c, model));
                    column.Item().PaddingTop(8).Element(c => ComposeMetadataBar(c, model));
                    column.Item().PaddingTop(8).Element(c => ComposeLineItemsTable(c, model));
                    column.Item().PaddingTop(10).Element(c => ComposeSummary(c, model));
                    column.Item().PaddingTop(8).Text($"Amount in words:: {model.AmountInWords}")
                        .FontSize(9).SemiBold();
                    column.Item().PaddingTop(18).AlignCenter().Text(model.FooterNotice)
                        .Bold().FontSize(9);
                    column.Item().PaddingTop(4).AlignCenter()
                        .Text($"FBR Digital Invoice - Printed {model.PrintedAt:dd/MM/yyyy HH:mm}")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();

    private static void ComposeHeader(IContainer container, SalesInvoicePrintDto model)
    {
        var qrBytes = GenerateQrCode(model.FbrInvoiceNumber ?? model.InvoiceNumber);
        var fbrNumber = model.FbrInvoiceNumber ?? model.InvoiceNumber;

        container.Row(row =>
        {
            row.ConstantItem(95).Column(left =>
            {
                left.Item().Border(1).BorderColor(GreenDark).Padding(4).Column(logo =>
                {
                    logo.Item().AlignCenter().Text("FBR").Bold().FontSize(14).FontColor(GreenDark);
                    logo.Item().AlignCenter().Text("DIGITAL").FontSize(7).SemiBold();
                    logo.Item().AlignCenter().Text("INVOICING").FontSize(7).SemiBold();
                    logo.Item().AlignCenter().Text("SYSTEM").FontSize(7).SemiBold();
                });
            });

            row.RelativeItem().PaddingHorizontal(8).Column(center =>
            {
                center.Item().AlignCenter().Text(model.Seller.Name).Bold().FontSize(18);
                center.Item().AlignCenter().Text("SALES TAX INVOICE").Bold().FontSize(16).FontColor(GreenDark);
                center.Item().PaddingTop(4).Background(GrayBox).Border(1).BorderColor(BorderGray)
                    .PaddingVertical(6).PaddingHorizontal(8).AlignCenter()
                    .Text($"FBR DIGITAL INVOICE #{fbrNumber}").SemiBold().FontSize(9);
            });

            row.ConstantItem(88).AlignRight().AlignTop().Width(88).Height(88).Image(qrBytes);
        });
    }

    private static void ComposePartyBoxes(IContainer container, SalesInvoicePrintDto model)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(c => PartyBox(
                c,
                "Seller Information",
                model.Seller.Name,
                FormatAddress(model.Seller.Address, model.Seller.Province),
                FbrInvoiceLayout.ResolveNtnCnic(model.Seller.Ntn, model.Seller.Cnic)));

            row.ConstantItem(10);

            row.RelativeItem().Element(c => PartyBox(
                c,
                "Buyer Information",
                model.Buyer.Name,
                FormatAddress(model.Buyer.Address, null),
                FbrInvoiceLayout.ResolveNtnCnic(model.Buyer.Ntn, model.Buyer.Cnic),
                model.Buyer.Province));
        });
    }

    private static void PartyBox(
        IContainer container,
        string title,
        string name,
        string? address,
        string ntnCnic,
        string? province = null)
    {
        container.Border(1).BorderColor(BorderGray).Padding(10).Column(box =>
        {
            box.Item().Text(title).Bold().FontSize(10).FontColor(GreenDark);
            box.Item().PaddingTop(6).Text(text =>
            {
                text.Span("Name: ").SemiBold();
                text.Span(name);
            });
            if (!string.IsNullOrWhiteSpace(address))
            {
                box.Item().PaddingTop(2).Text(text =>
                {
                    text.Span("Address: ").SemiBold();
                    text.Span(address);
                });
            }

            if (!string.IsNullOrWhiteSpace(province))
            {
                box.Item().PaddingTop(2).Text(text =>
                {
                    text.Span("Province: ").SemiBold();
                    text.Span(province.ToUpperInvariant());
                });
            }

            box.Item().PaddingTop(2).Text(text =>
            {
                text.Span("NTN / CNIC: ").SemiBold();
                text.Span(ntnCnic);
            });
        });
    }

    private static void ComposeMetadataBar(IContainer container, SalesInvoicePrintDto model)
    {
        container.Background(GreenLight).Border(1).BorderColor(BorderGray).Padding(8).Row(row =>
        {
            MetadataField(row.RelativeItem(), "Invoice date", model.InvoiceDate.ToString("dd/MM/yyyy"));
            MetadataField(row.RelativeItem(), "Invoice type", model.InvoiceTypeLabel);
            MetadataField(row.RelativeItem(), "Tax period", model.TaxPeriod);
            MetadataField(row.RelativeItem(), "Reference no.", model.InvoiceNumber);
        });
    }

    private static void MetadataField(IContainer container, string label, string value)
    {
        container.Column(col =>
        {
            col.Item().Text(label).SemiBold().FontSize(8).FontColor(GreenDark);
            col.Item().Text(value).FontSize(9);
        });
    }

    private static void ComposeLineItemsTable(IContainer container, SalesInvoicePrintDto model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(18);
                columns.RelativeColumn(2.4f);
                columns.RelativeColumn(0.9f);
                columns.RelativeColumn(1.4f);
                columns.RelativeColumn(0.8f);
                columns.ConstantColumn(28);
                columns.ConstantColumn(30);
                columns.RelativeColumn(0.9f);
                columns.RelativeColumn(0.9f);
                columns.RelativeColumn(0.8f);
                columns.RelativeColumn(0.9f);
            });

            table.Header(header =>
            {
                HeaderCell(header.Cell(), "Sr");
                HeaderCell(header.Cell(), "Product");
                HeaderCell(header.Cell(), "HS code");
                HeaderCell(header.Cell(), "Sale type");
                HeaderCell(header.Cell(), "Qty");
                HeaderCell(header.Cell(), "UoM");
                HeaderCell(header.Cell(), "Tax %");
                HeaderCell(header.Cell(), "Val ex ST");
                HeaderCell(header.Cell(), "Sales ST");
                HeaderCell(header.Cell(), "F. tax");
                HeaderCell(header.Cell(), "Total");
            });

            foreach (var line in model.Lines)
            {
                BodyCell(table.Cell(), line.LineNo.ToString());
                BodyCell(table.Cell(), line.ProductDisplay);
                BodyCell(table.Cell(), line.HsCode ?? "—");
                BodyCell(table.Cell(), line.SaleType, 7.5f);
                BodyCell(table.Cell(), FormatNumber(line.Quantity), alignRight: true);
                BodyCell(table.Cell(), line.Unit ?? "—");
                BodyCell(table.Cell(), line.TaxRateDisplay, alignRight: true);
                BodyCell(table.Cell(), FormatNumber(line.ValueExcludingSt), alignRight: true);
                BodyCell(table.Cell(), FormatNumber(line.SalesTax), alignRight: true);
                BodyCell(table.Cell(), FormatNumber(line.FurtherTax), alignRight: true);
                BodyCell(table.Cell(), FormatNumber(line.LineTotal), alignRight: true);
            }
        });
    }

    private static void ComposeSummary(IContainer container, SalesInvoicePrintDto model)
    {
        container.AlignRight().Width(230).Border(1).BorderColor(BorderGray).Background(GrayBox).Padding(10)
            .Column(summary =>
            {
                SummaryRow(summary, "Exclusive total amount", model.ExclusiveTotal);
                SummaryRow(summary, "Sales tax", model.SalesTaxTotal);
                summary.Item().PaddingTop(4).BorderTop(1).BorderColor(BorderGray).Row(row =>
                {
                    row.RelativeItem().Text("Inclusive total amount").Bold().FontSize(10);
                    row.ConstantItem(90).AlignRight().Text(FormatNumber(model.InclusiveTotal))
                        .Bold().FontSize(12).FontColor(GreenDark);
                });
            });
    }

    private static void SummaryRow(ColumnDescriptor column, string label, decimal amount)
    {
        column.Item().PaddingBottom(3).Row(row =>
        {
            row.RelativeItem().Text(label).SemiBold();
            row.ConstantItem(90).AlignRight().Text(FormatNumber(amount));
        });
    }

    private static void HeaderCell(IContainer container, string text) =>
        container.Background(Colors.White).BorderBottom(1).BorderColor(BorderGray)
            .PaddingVertical(5).PaddingHorizontal(2).Text(text).SemiBold().FontSize(8);

    private static void BodyCell(
        IContainer container,
        string text,
        float fontSize = 8f,
        bool alignRight = false)
    {
        var cell = container.BorderBottom(1).BorderColor(Colors.Grey.Lighten3)
            .PaddingVertical(4).PaddingHorizontal(2);

        if (alignRight)
        {
            cell.AlignRight().Text(text).FontSize(fontSize);
            return;
        }

        cell.Text(text).FontSize(fontSize);
    }

    private static string FormatAddress(string? address, string? province)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(address))
        {
            parts.Add(address.Trim());
        }

        if (!string.IsNullOrWhiteSpace(province))
        {
            parts.Add(province.Trim());
        }

        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    private static string FormatNumber(decimal value) =>
        value.ToString("N2", NumberCulture);

    private static byte[] GenerateQrCode(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(data);
        return qrCode.GetGraphic(4);
    }
}
