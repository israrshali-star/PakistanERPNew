using System.Globalization;
using System.Net;
using System.Text;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Services;

/// <summary>
/// Prints on A4, which is in the printer's paper list. The cheque is turned so
/// the date end is the top of the page. Feed that short edge in first. Each
/// line is already inside the centre 88.9 mm band, so the writing meets a
/// cheque that sits in the centre of the feeder.
/// </summary>
public static class ChequePrintHtml
{
    public static string Build(ChequePrintDto model)
    {
        var pageWidth = ChequeLeafLayout.A4WidthMm;
        var pageHeight = ChequeLeafLayout.A4HeightMm;
        var inks = ChequeLeafFields.Place(model);
        var bandClass = model.CentreInFeeder ? "centre" : "left";
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<!-- cheque-layout:a4-date-first cheque-nudges-2026-10-06 -->");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\" />");
        html.AppendLine("<title>Cheque</title>");
        html.AppendLine("<style>");
        html.AppendLine("@page { size: A4 portrait; margin: 0; }");
        html.AppendLine("html, body { margin: 0; padding: 0; width: " + Mm(pageWidth) + "; height: " + Mm(pageHeight) + "; overflow: hidden; background: #fff; }");
        // The band is the cheque as it sits in the feeder: 88.9 mm across, date end at the top.
        // Centre uses half the page, not a 190 mm box parked on the right and then rotated.
        html.AppendLine(".band { position: fixed; top: 0; width: " + Mm(ChequeLeafLayout.FeedSpanMm) + "; height: " + Mm(ChequeLeafLayout.WidthMm) + "; }");
        html.AppendLine(".band.centre { left: 50%; margin-left: -" + Mm(ChequeLeafLayout.FeedSpanMm / 2f) + "; }");
        html.AppendLine(".band.left { left: 0; }");
        html.AppendLine(".slot { position: absolute; margin: 0; padding: 0; }");
        html.AppendLine(".turn { position: absolute; left: 0; top: 0; box-sizing: border-box; margin: 0; padding: 0; color: #111; font-family: 'Times New Roman', 'Liberation Serif', serif; line-height: 1; white-space: nowrap; overflow: hidden; transform-origin: top left; }");
        html.AppendLine(".on-rule { display: flex; align-items: flex-end; justify-content: flex-start; }");
        html.AppendLine(".middle-left { display: flex; align-items: center; justify-content: flex-start; }");
        html.AppendLine(".middle-center, .crossing { display: flex; align-items: center; justify-content: center; }");
        html.AppendLine(".crossing { border-top: 0.6pt solid #111; border-bottom: 0.6pt solid #111; }");
        html.AppendLine(".bold { font-weight: 700; }");
        html.AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.Append("<div class=\"band ").Append(bandClass).AppendLine("\">");
        foreach (var ink in inks)
        {
            if (string.IsNullOrWhiteSpace(ink.Text))
            {
                continue;
            }

            var align = ink.Align switch
            {
                ChequeInkAlign.OnRule => "on-rule",
                ChequeInkAlign.MiddleLeft => "middle-left",
                ChequeInkAlign.Crossing => "crossing",
                _ => "middle-center"
            };
            var weight = ink.Bold ? " bold" : string.Empty;
            // Leaf x runs toward the date (up the page). Leaf y runs down the cheque (across the page).
            var pageLeft = ink.TopMm;
            var pageTop = ChequeLeafLayout.WidthMm - (ink.LeftMm + ink.WidthMm);
            html.Append("<div class=\"slot\" style=\"left:")
                .Append(Mm(pageLeft))
                .Append(";top:")
                .Append(Mm(pageTop))
                .Append(";width:")
                .Append(Mm(ink.HeightMm))
                .Append(";height:")
                .Append(Mm(ink.WidthMm))
                .AppendLine(";\">");
            html.Append("<div class=\"turn ")
                .Append(align)
                .Append(weight)
                .Append("\" style=\"width:")
                .Append(Mm(ink.WidthMm))
                .Append(";height:")
                .Append(Mm(ink.HeightMm))
                .Append(";font-size:")
                .Append(ink.FontSizePt.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("pt;transform:translateY(")
                .Append(Mm(ink.WidthMm))
                .Append(") rotate(-90deg);\">")
                .Append(WebUtility.HtmlEncode(ink.Text))
                .AppendLine("</div></div>");
        }

        html.AppendLine("</div>");
        html.AppendLine("<script>");
        html.AppendLine("window.__chequePrint = function () {");
        html.AppendLine("  if (window.__chequePrinted) return;");
        html.AppendLine("  window.__chequePrinted = true;");
        html.AppendLine("  window.focus();");
        html.AppendLine("  window.print();");
        html.AppendLine("};");
        html.AppendLine("window.addEventListener('load', function () { setTimeout(window.__chequePrint, 200); });");
        html.AppendLine("</script>");
        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static string Mm(float value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "mm";
}
