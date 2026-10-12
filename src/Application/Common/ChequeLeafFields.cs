using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Common;

public enum ChequeInkAlign
{
    OnRule,
    MiddleLeft,
    MiddleCenter,
    Crossing
}

public readonly record struct ChequeInk(
    string Text,
    float LeftMm,
    float TopMm,
    float WidthMm,
    float HeightMm,
    float FontSizePt,
    bool Bold,
    ChequeInkAlign Align);

/// <summary>
/// Places cheque ink in millimetres on the leaf as it is read.
/// The print page turns this leaf so the date end is the leading edge.
/// </summary>
public static class ChequeLeafFields
{
    private const float PointsPerMm = 72f / 25.4f;

    public static IReadOnlyList<ChequeInk> Place(ChequePrintDto model)
    {
        var offsetX = ClampShift(model.OffsetXmm);
        var offsetY = ClampShift(model.OffsetYmm);
        var dateX = offsetX + ClampShift(model.DateOffsetXmm);
        var dateY = offsetY + ClampShift(model.DateOffsetYmm);
        var payeeWordsX = offsetX + ClampShift(model.PayWordsOffsetXmm);
        var payeeWordsY = offsetY + ClampShift(model.PayWordsOffsetYmm);
        var inks = new List<ChequeInk>();

        if (!string.Equals(model.PayeeName, "Cash", StringComparison.OrdinalIgnoreCase))
        {
            inks.Add(new ChequeInk(
                "A/C PAYEE ONLY",
                ChequeLeafLayout.CrossingLeftMm + offsetX,
                ChequeLeafLayout.CrossingTopMm + offsetY,
                ChequeLeafLayout.CrossingWidthMm,
                ChequeLeafLayout.CrossingHeightMm,
                7f,
                true,
                ChequeInkAlign.Crossing));
        }

        var digits = model.ChequeDate.ToString("ddMMyyyy", System.Globalization.CultureInfo.InvariantCulture);
        for (var i = 0; i < digits.Length; i++)
        {
            inks.Add(new ChequeInk(
                digits[i].ToString(),
                ChequeLeafLayout.DateLeftMm + (i * ChequeLeafLayout.DateBoxPitchMm) + dateX,
                ChequeLeafLayout.DateTopMm + dateY,
                ChequeLeafLayout.DateBoxPitchMm,
                ChequeLeafLayout.DateHeightMm,
                12f,
                true,
                ChequeInkAlign.MiddleCenter));
        }

        AddOnRule(
            inks,
            ChequeLeafLayout.PayeeLeftMm,
            ChequeLeafLayout.PayeeLineMm,
            ChequeLeafLayout.PayeeWidthMm,
            model.PayeeName,
            FitFontSize(model.PayeeName, ChequeLeafLayout.PayeeWidthMm, lines: 1, maxSize: 11f, minSize: 8f),
            bold: true,
            payeeWordsX,
            payeeWordsY);
        AddWords(inks, model.AmountInWords, payeeWordsX, payeeWordsY);
        inks.Add(new ChequeInk(
            model.AmountFigures.Trim(),
            ChequeLeafLayout.FiguresLeftMm + ChequeLeafLayout.FiguresTextInsetMm + offsetX,
            ChequeLeafLayout.FiguresTopMm + offsetY,
            ChequeLeafLayout.FiguresWidthMm - ChequeLeafLayout.FiguresTextInsetMm - ChequeLeafLayout.FiguresTextRightPadMm,
            ChequeLeafLayout.FiguresHeightMm,
            FitFontSize(model.AmountFigures, ChequeLeafLayout.FiguresWidthMm - ChequeLeafLayout.FiguresTextInsetMm, lines: 1, maxSize: 11f, minSize: 8f),
            true,
            ChequeInkAlign.MiddleLeft));

        return inks;
    }

    private static float ClampShift(float value) =>
        Math.Clamp(value, -ChequeLeafLayout.MaxShiftMm, ChequeLeafLayout.MaxShiftMm);

    private static void AddWords(List<ChequeInk> inks, string? words, float offsetX, float offsetY)
    {
        if (string.IsNullOrWhiteSpace(words))
        {
            return;
        }

        var text = words.Trim();
        var fontSize = 10f;
        List<string> lines = [];
        while (fontSize >= 7.5f)
        {
            var maxChars = MaxChars(ChequeLeafLayout.WordsWidthMm, fontSize);
            lines = SplitToLines(text, maxChars, maxLines: 2);
            if (string.Join(' ', lines) == text || fontSize <= 7.5f)
            {
                break;
            }

            fontSize -= 0.5f;
        }

        if (lines.Count > 0)
        {
            AddOnRule(inks, ChequeLeafLayout.WordsLeftMm, ChequeLeafLayout.WordsLine1Mm, ChequeLeafLayout.WordsWidthMm, lines[0], fontSize, false, offsetX, offsetY);
        }

        if (lines.Count > 1)
        {
            AddOnRule(inks, ChequeLeafLayout.WordsLeftMm, ChequeLeafLayout.WordsLine2Mm, ChequeLeafLayout.WordsWidthMm, lines[1], fontSize, false, offsetX, offsetY);
        }
    }

    private static void AddOnRule(
        List<ChequeInk> inks,
        float leftMm,
        float ruleMm,
        float widthMm,
        string text,
        float fontSize,
        bool bold,
        float offsetX,
        float offsetY)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var emMm = fontSize / PointsPerMm;
        inks.Add(new ChequeInk(
            text.Trim(),
            leftMm + offsetX,
            ruleMm - emMm - 0.4f + offsetY,
            widthMm,
            emMm,
            fontSize,
            bold,
            ChequeInkAlign.OnRule));
    }

    private static float FitFontSize(string? text, float widthMm, int lines, float maxSize, float minSize)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return maxSize;
        }

        var size = maxSize;
        var length = text.Trim().Length;
        while (size > minSize && length > MaxChars(widthMm, size) * lines)
        {
            size -= 0.5f;
        }

        return size;
    }

    private static int MaxChars(float widthMm, float fontSize)
    {
        var charMm = fontSize / PointsPerMm * 0.55f;
        return Math.Max(8, (int)Math.Floor(widthMm / charMm));
    }

    private static List<string> SplitToLines(string text, int maxChars, int maxLines)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            var next = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && next.Length > maxChars)
            {
                lines.Add(current);
                current = word;
                if (lines.Count >= maxLines)
                {
                    current = string.Empty;
                    break;
                }

                continue;
            }

            current = next;
        }

        if (current.Length > 0 && lines.Count < maxLines)
        {
            lines.Add(current);
        }

        return lines;
    }
}
