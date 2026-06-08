using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace PakistanAccountingERP.Application.Services;

public class DeliveryChallanPdfService : IDeliveryChallanPdfService
{
    private static readonly Color GreenDark = Color.FromHex("#1F6B45");
    private static readonly Color BorderGray = Color.FromHex("#D0D0D0");
    private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-PK");

    static DeliveryChallanPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GeneratePdf(DeliveryChallanPrintDto model) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(210, 148, Unit.Millimetre);
                page.Margin(16);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Arial"));

                page.Content().Column(column =>
                {
                    column.Item().Element(c => ComposeHeader(c, model));
                    column.Item().PaddingTop(6).Element(c => ComposeParties(c, model));
                    column.Item().PaddingTop(6).Element(c => ComposeLinesTable(c, model));
                    column.Item().PaddingTop(8).Element(c => ComposeSignatures(c));
                    column.Item().PaddingTop(4).AlignCenter()
                        .Text($"Delivery Challan — {model.InvoiceNumber} — Printed {model.PrintedAt:dd/MM/yyyy HH:mm}")
                        .FontSize(7).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();

    private static void ComposeHeader(IContainer container, DeliveryChallanPrintDto model)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(model.SellerName).Bold().FontSize(12).FontColor(GreenDark);
                left.Item().Text("DELIVERY CHALLAN").Bold().FontSize(11);
                if (!string.IsNullOrWhiteSpace(model.SellerAddress))
                {
                    left.Item().Text(model.SellerAddress).FontSize(7);
                }
            });

            row.ConstantItem(120).AlignRight().Column(right =>
            {
                right.Item().Text($"Challan Ref: {model.InvoiceNumber}").SemiBold();
                right.Item().Text($"Date: {model.InvoiceDate:dd/MM/yyyy}");
                if (!string.IsNullOrWhiteSpace(model.SellerPhone))
                {
                    right.Item().Text($"Phone: {model.SellerPhone}");
                }
            });
        });
    }

    private static void ComposeParties(IContainer container, DeliveryChallanPrintDto model)
    {
        var buyerNtnCnic = FbrInvoiceLayout.ResolveNtnCnic(model.BuyerNtn, model.BuyerCnic);

        container.Border(1).BorderColor(BorderGray).Padding(8).Column(box =>
        {
            box.Item().Text("Deliver To").Bold().FontColor(GreenDark);
            box.Item().PaddingTop(4).Text(text =>
            {
                text.Span("Buyer: ").SemiBold();
                text.Span(model.BuyerName);
            });
            if (!string.IsNullOrWhiteSpace(model.BuyerAddress))
            {
                box.Item().Text($"Address: {model.BuyerAddress}");
            }

            if (!string.IsNullOrWhiteSpace(model.BuyerProvince))
            {
                box.Item().Text($"Province: {model.BuyerProvince}");
            }

            box.Item().Text($"NTN / CNIC: {buyerNtnCnic}");
        });
    }

    private static void ComposeLinesTable(IContainer container, DeliveryChallanPrintDto model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(22);
                columns.RelativeColumn(3);
                columns.ConstantColumn(42);
                columns.ConstantColumn(42);
                columns.ConstantColumn(42);
                columns.ConstantColumn(48);
                columns.ConstantColumn(30);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Sr");
                header.Cell().Element(HeaderCell).Text("Description");
                header.Cell().Element(HeaderCell).Text("Lot No");
                header.Cell().Element(HeaderCell).Text("Stack No");
                header.Cell().Element(HeaderCell).AlignRight().Text("Cartons");
                header.Cell().Element(HeaderCell).AlignRight().Text("Qty");
                header.Cell().Element(HeaderCell).Text("UoM");
            });

            foreach (var line in model.Lines)
            {
                table.Cell().Element(BodyCell).Text(line.LineNo.ToString());
                table.Cell().Element(BodyCell).Text(line.ItemDescription);
                table.Cell().Element(BodyCell).Text(line.LotNo ?? "—");
                table.Cell().Element(BodyCell).Text(line.StackNo ?? "—");
                table.Cell().Element(BodyCell).AlignRight().Text(FormatQty(line.Cartons, true));
                table.Cell().Element(BodyCell).AlignRight().Text(FormatQty(line.Quantity, false));
                table.Cell().Element(BodyCell).Text(line.Unit ?? "—");
            }
        });
    }

    private static void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().PaddingTop(16).BorderTop(1).BorderColor(BorderGray).PaddingTop(4)
                    .Text("Prepared By").FontSize(7);
            });
            row.ConstantItem(20);
            row.RelativeItem().Column(col =>
            {
                col.Item().PaddingTop(16).BorderTop(1).BorderColor(BorderGray).PaddingTop(4)
                    .Text("Received By").FontSize(7);
            });
            row.ConstantItem(20);
            row.RelativeItem().Column(col =>
            {
                col.Item().PaddingTop(16).BorderTop(1).BorderColor(BorderGray).PaddingTop(4)
                    .Text("Authorized Signatory").FontSize(7);
            });
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(BorderGray)
            .PaddingVertical(4).PaddingHorizontal(3).DefaultTextStyle(x => x.SemiBold().FontSize(7));

    private static IContainer BodyCell(IContainer container) =>
        container.Border(0.5f).BorderColor(BorderGray).PaddingVertical(3).PaddingHorizontal(3)
            .DefaultTextStyle(x => x.FontSize(7));

    private static string FormatQty(decimal value, bool wholeNumber) =>
        wholeNumber
            ? value.ToString("N0", NumberCulture)
            : value.ToString("N2", NumberCulture);
}
