using System.Globalization;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PakistanAccountingERP.Application.Services;

public class VendorPaymentPdfService : IVendorPaymentPdfService
{
    private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-PK");

    static VendorPaymentPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GeneratePdf(VendorPaymentPdfDto model) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(210, 148, Unit.Millimetre);
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Content().Column(column =>
                {
                    column.Item().AlignCenter().Text("Payment Voucher").Bold().FontSize(14);
                    column.Item().PaddingTop(4).AlignCenter().Text(model.CompanyName).SemiBold();

                    column.Item().PaddingTop(12).Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text($"Payment #: {model.PaymentNumber}").Bold();
                            left.Item().PaddingTop(4).Text($"Date: {model.PaymentDate:dd/MM/yyyy}");
                        });
                        row.RelativeItem().AlignRight().Column(right =>
                        {
                            right.Item().AlignRight().Text($"Amount: PKR {FormatAmount(model.Amount)}").Bold().FontSize(11);
                            right.Item().PaddingTop(4).AlignRight().Text($"Payment: {model.PaymentMethodLabel}");
                        });
                    });

                    column.Item().PaddingTop(8).Text($"Amount in words: {AmountInWords.ToPakistaniRupees(model.Amount)}")
                        .Italic().FontSize(8);

                    column.Item().PaddingTop(12).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                    column.Item().PaddingTop(10).Text("Paid To").SemiBold().FontSize(8).FontColor(Colors.Grey.Darken1);
                    column.Item().PaddingTop(2).Text($"{model.VendorName} ({model.VendorCode})").Bold();

                    if (!string.IsNullOrWhiteSpace(model.BankName))
                    {
                        column.Item().PaddingTop(6).Text($"Bank: {model.BankName}");
                    }

                    if (!string.IsNullOrWhiteSpace(model.ChequeNumber))
                    {
                        column.Item().PaddingTop(2).Text(
                            $"Cheque #: {model.ChequeNumber}" +
                            (model.ChequeDate.HasValue ? $" · Date: {model.ChequeDate.Value:dd/MM/yyyy}" : string.Empty));
                    }

                    if (!string.IsNullOrWhiteSpace(model.Notes))
                    {
                        column.Item().PaddingTop(6).Text($"Notes: {model.Notes}");
                    }

                    column.Item().PaddingTop(16).Row(row =>
                    {
                        row.RelativeItem().Column(sig =>
                        {
                            sig.Item().LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                            sig.Item().PaddingTop(2).Text("Paid By").FontSize(8);
                        });
                        row.ConstantItem(20);
                        row.RelativeItem().Column(sig =>
                        {
                            sig.Item().LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                            sig.Item().PaddingTop(2).Text("Authorized Signature").FontSize(8);
                        });
                    });

                    column.Item().PaddingTop(8).AlignCenter()
                        .Text($"Printed {DateTime.Now:dd/MM/yyyy HH:mm}")
                        .FontSize(7).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();

    private static string FormatAmount(decimal value) =>
        value.ToString("N2", NumberCulture);
}
