using System.Globalization;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PakistanAccountingERP.Application.Services;

public class CustomerReceiptPdfService : ICustomerReceiptPdfService
{
    private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-PK");
    private static readonly Color BorderColor = Color.FromHex("#333333");

    static CustomerReceiptPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GeneratePdf(CustomerReceiptPdfDto model) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                page.Content().Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Text(model.CompanyName).FontSize(11);
                        row.RelativeItem().AlignRight().Text("Payment Receipt").Bold().FontSize(16);
                    });

                    column.Item().PaddingTop(18).Element(c => ComposeReceivedFromBox(c, model));
                    column.Item().PaddingTop(14).Row(row =>
                    {
                        row.RelativeItem().Element(c => ComposeLeftDetailsTable(c, model));
                        row.ConstantItem(12);
                        row.RelativeItem().Element(c => ComposeRightAmountTable(c, model));
                    });

                    column.Item().PaddingTop(16).Text("Invoices Paid").Bold().FontSize(11);
                    column.Item().PaddingTop(6).Element(ComposeInvoicesPaidTable);

                    column.Item().PaddingTop(10).AlignRight()
                        .Text($"Receipt #: {model.ReceiptNumber}  ·  Printed {DateTime.Now:dd/MM/yyyy HH:mm}")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();

    private static void ComposeReceivedFromBox(IContainer container, CustomerReceiptPdfDto model)
    {
        container.Border(1).BorderColor(BorderColor).Padding(10).Column(box =>
        {
            box.Item().AlignCenter().Text("Received From").Bold().FontSize(11);
            box.Item().PaddingTop(8).AlignCenter().Text(model.CustomerName).Bold().FontSize(13);
        });
    }

    private static void ComposeLeftDetailsTable(IContainer container, CustomerReceiptPdfDto model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1.1f);
                columns.RelativeColumn(1.4f);
            });

            AddDetailRow(table, "Date", model.ReceiptDate.ToString("dd/MM/yyyy"));
            AddDetailRow(table, "Payment Method", model.PaymentMethodLabel);
            AddDetailRow(table, "Check/Ref No", string.IsNullOrWhiteSpace(model.ChequeNumber) ? "—" : model.ChequeNumber);
            AddDetailRow(
                table,
                "Amount in words",
                AmountInWords.ToPakistaniRupees(model.Amount),
                valueFontSize: 8);
        });
    }

    private static void ComposeRightAmountTable(IContainer container, CustomerReceiptPdfDto model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1.2f);
                columns.RelativeColumn(1f);
            });

            AddDetailRow(table, "Payment Amount", $"PKR {FormatAmount(model.Amount)}", valueBold: true);
            AddDetailRow(table, "Total Amount Due", $"PKR {FormatAmount(model.TotalAmountDue)}", valueBold: true);
        });
    }

    private static void ComposeInvoicesPaidTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1.2f);
                columns.RelativeColumn(1f);
                columns.RelativeColumn(1f);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Invoice #");
                header.Cell().Element(HeaderCell).Text("Date");
                header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
            });

            table.Cell().ColumnSpan(3).Element(BodyCell).AlignCenter()
                .Text("—").FontColor(Colors.Grey.Darken1);
        });
    }

    private static void AddDetailRow(
        TableDescriptor table,
        string label,
        string value,
        bool valueBold = false,
        float valueFontSize = 10)
    {
        table.Cell().Element(LabelCell).Text(label);
        table.Cell().Element(BodyCell).Text(text =>
        {
            var span = text.Span(value).FontSize(valueFontSize);
            if (valueBold)
            {
                span.Bold();
            }
        });
    }

    private static IContainer LabelCell(IContainer container) =>
        container.Border(1).BorderColor(BorderColor).Padding(6).DefaultTextStyle(x => x.SemiBold());

    private static IContainer BodyCell(IContainer container) =>
        container.Border(1).BorderColor(BorderColor).Padding(6);

    private static IContainer HeaderCell(IContainer container) =>
        container.Border(1).BorderColor(BorderColor).Padding(6).DefaultTextStyle(x => x.SemiBold().FontSize(9));

    private static string FormatAmount(decimal value) =>
        value.ToString("N2", NumberCulture);
}
