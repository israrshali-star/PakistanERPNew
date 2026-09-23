using System.Globalization;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace PakistanAccountingERP.Application.Services;

/// <summary>
/// Prints fill-in fields onto a standard Pakistani cheque leaf (SBP 175×82 mm),
/// such as Meezan Bank Premium. Only date, payee, amount-in-words, and figures
/// are drawn — bank pre-print, MICR, and signature stay blank.
/// Landscape matches the cheque as you write it. Portrait is the same artwork
/// rotated for a printer that feeds the cheque short-edge first.
/// </summary>
public class ChequePrintPdfService : IChequePrintPdfService
{
    private const float LeafWidthMm = 175f;
    private const float LeafHeightMm = 82f;

    static ChequePrintPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GeneratePdf(ChequePrintDto model) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                var landscape = model.Orientation != ChequePageOrientation.Portrait;
                if (landscape)
                {
                    page.Size(LeafWidthMm, LeafHeightMm, Unit.Millimetre);
                }
                else
                {
                    page.Size(LeafHeightMm, LeafWidthMm, Unit.Millimetre);
                }

                page.Margin(0);
                page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontColor("#111111"));

                page.Content().Element(content =>
                {
                    if (landscape)
                    {
                        ComposeLeaf(content, model);
                        return;
                    }

                    content
                        .RotateLeft()
                        .Width(LeafWidthMm, Unit.Millimetre)
                        .Height(LeafHeightMm, Unit.Millimetre)
                        .Element(inner => ComposeLeaf(inner, model));
                });
            });
        }).GeneratePdf();

    private static void ComposeLeaf(IContainer container, ChequePrintDto model)
    {
        container.Layers(layers =>
        {
            layers.PrimaryLayer();
            PlaceDateDigits(layers, model.ChequeDate);
            PlaceText(layers, 22, 23, 98, model.PayeeName, 12, true);
            PlaceText(layers, 28, 38, 88, model.AmountInWords, 10, false);
            PlaceText(layers, 118, 36, 42, model.AmountFigures, 12, true);
        });
    }

    private static void PlaceDateDigits(LayersDescriptor layers, DateTime date)
    {
        var digits = date.ToString("ddMMyyyy", CultureInfo.InvariantCulture);
        for (var i = 0; i < digits.Length; i++)
        {
            PlaceText(layers, 122.5f + (i * 5.9f), 7.2f, 6, digits[i].ToString(), 12, true);
        }
    }

    private static void PlaceText(
        LayersDescriptor layers,
        float leftMm,
        float topMm,
        float widthMm,
        string text,
        float fontSize,
        bool bold)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var textBlock = layers.Layer()
            .Unconstrained()
            .TranslateX(leftMm, Unit.Millimetre)
            .TranslateY(topMm, Unit.Millimetre)
            .Width(widthMm, Unit.Millimetre)
            .Text(text.Trim())
            .FontSize(fontSize);

        if (bold)
        {
            textBlock.Bold();
        }
    }
}
