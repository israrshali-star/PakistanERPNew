namespace PakistanAccountingERP.Application.Common;

public static class AmountInWords
{
    private static readonly string[] Ones =
    [
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
        "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
        "Seventeen", "Eighteen", "Nineteen"
    ];

    private static readonly string[] Tens =
    [
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
    ];

    public static string ToPakistaniRupees(decimal amount) =>
        FormatRupees(amount, useMillionScale: false);

    /// <summary>Amount in words using million/billion (not lakh/crore), e.g. 2,500,000 → Two Million Five Hundred Thousand Rupees Only.</summary>
    public static string ToRupeesInMillions(decimal amount) =>
        FormatRupees(amount, useMillionScale: true);

    private static string FormatRupees(decimal amount, bool useMillionScale)
    {
        var rupees = (long)Math.Floor(amount);
        var paisa = (int)Math.Round((amount - rupees) * 100m, 0);

        if (rupees == 0 && paisa == 0)
        {
            return "Zero Rupees Only";
        }

        var words = ConvertNumber(rupees, useMillionScale);
        var result = string.IsNullOrWhiteSpace(words) ? string.Empty : $"{words} Rupees";

        if (paisa > 0)
        {
            var paisaWords = ConvertNumber(paisa, useMillionScale);
            result += string.IsNullOrWhiteSpace(paisaWords)
                ? string.Empty
                : $" and {paisaWords} Paisa";
        }

        return $"{result} Only".Trim();
    }

    private static string ConvertNumber(long number, bool useMillionScale)
    {
        if (number == 0)
        {
            return string.Empty;
        }

        if (number < 0)
        {
            return $"Minus {ConvertNumber(Math.Abs(number), useMillionScale)}";
        }

        var parts = new List<string>();

        if (useMillionScale)
        {
            AppendScale(parts, ref number, 1_000_000_000_000, "Trillion", useMillionScale);
            AppendScale(parts, ref number, 1_000_000_000, "Billion", useMillionScale);
            AppendScale(parts, ref number, 1_000_000, "Million", useMillionScale);
        }
        else
        {
            AppendScale(parts, ref number, 10_000_000, "Crore", useMillionScale);
            AppendScale(parts, ref number, 100_000, "Lakh", useMillionScale);
        }

        AppendScale(parts, ref number, 1_000, "Thousand", useMillionScale);
        AppendScale(parts, ref number, 100, "Hundred", useMillionScale);

        if (number >= 20)
        {
            parts.Add(Tens[number / 10]);
            number %= 10;
        }

        if (number > 0)
        {
            parts.Add(Ones[number]);
        }

        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static void AppendScale(
        List<string> parts,
        ref long number,
        long scale,
        string label,
        bool useMillionScale)
    {
        if (number < scale)
        {
            return;
        }

        var count = number / scale;
        number %= scale;
        var words = ConvertNumber(count, useMillionScale);
        if (!string.IsNullOrWhiteSpace(words))
        {
            parts.Add($"{words} {label}");
        }
    }
}
