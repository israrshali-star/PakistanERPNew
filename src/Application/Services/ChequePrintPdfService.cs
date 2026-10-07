using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace PakistanAccountingERP.Application.Services;

/// <summary>
/// Prints fill-in fields onto a Meezan / SBP cheque leaf (7.5 × 3.5 in).
/// Date digits are centered in the eight boxes, the payee sits on the Pay rule,
/// the amount sits in the figures box, and the words sit on the rupees rules.
/// Party cheques are crossed A/C Payee Only. MICR and the signature stay blank.
/// Landscape matches the cheque as you write it. Portrait is the same artwork
/// rotated for a printer that feeds the cheque short-edge first.
/// </summary>
public class ChequePrintPdfService : IChequePrintPdfService
{
    private static readonly string ChequeFontFamily = Initialize();

    private static string Initialize()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return ResolveChequeFont();
    }

    public byte[] GeneratePdf(ChequePrintDto model)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                var landscape = model.Orientation != ChequePageOrientation.Portrait;
                if (landscape)
                {
                    page.Size(ChequeLeafLayout.WidthMm, ChequeLeafLayout.HeightMm, Unit.Millimetre);
                }
                else
                {
                    page.Size(ChequeLeafLayout.HeightMm, ChequeLeafLayout.WidthMm, Unit.Millimetre);
                }

                page.Margin(0);
                page.DefaultTextStyle(style => style.FontFamily(ChequeFontFamily).FontColor("#111111").LineHeight(1));

                page.Content().Element(content =>
                {
                    if (landscape)
                    {
                        ComposeLeaf(content, model);
                        return;
                    }

                    content
                        .RotateLeft()
                        .Width(ChequeLeafLayout.WidthMm, Unit.Millimetre)
                        .Height(ChequeLeafLayout.HeightMm, Unit.Millimetre)
                        .Element(inner => ComposeLeaf(inner, model));
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeLeaf(IContainer container, ChequePrintDto model)
    {
        container.Layers(layers =>
        {
            layers.PrimaryLayer()
                .Width(ChequeLeafLayout.WidthMm, Unit.Millimetre)
                .Height(ChequeLeafLayout.HeightMm, Unit.Millimetre);

            foreach (var ink in ChequeLeafFields.Place(model))
            {
                DrawInk(layers, ink);
            }
        });
    }

    private static void DrawInk(LayersDescriptor layers, ChequeInk ink)
    {
        if (string.IsNullOrWhiteSpace(ink.Text))
        {
            return;
        }

        var box = layers.Layer()
            .Unconstrained()
            .TranslateX(ink.LeftMm, Unit.Millimetre)
            .TranslateY(ink.TopMm, Unit.Millimetre)
            .Width(ink.WidthMm, Unit.Millimetre)
            .Height(ink.HeightMm, Unit.Millimetre);

        if (ink.Align == ChequeInkAlign.Crossing)
        {
            box = box.BorderTop(0.6f).BorderBottom(0.6f);
        }

        box = ink.Align switch
        {
            ChequeInkAlign.OnRule => box.AlignBottom().AlignLeft(),
            ChequeInkAlign.MiddleLeft => box.AlignMiddle().AlignLeft(),
            _ => box.AlignMiddle().AlignCenter()
        };

        var span = box
            .Text(ink.Text)
            .FontFamily(ChequeFontFamily)
            .FontSize(ink.FontSizePt)
            .LineHeight(1);

        if (ink.Bold)
        {
            span.Bold();
        }
    }

    private static string ResolveChequeFont()
    {
        foreach (var family in new[] { "Times New Roman", "Liberation Serif", "Noto Serif", "Lato" })
        {
            if (CanUseFont(family))
            {
                return family;
            }
        }

        return "Lato";
    }

    private static bool CanUseFont(string family)
    {
        try
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(20, 20);
                    page.Content().Text("A").FontFamily(family).FontSize(8);
                });
            }).GeneratePdf();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
