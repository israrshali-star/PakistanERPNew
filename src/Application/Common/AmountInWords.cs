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

    public static string ToPakistaniRupees(decimal amount)
    {
        var rupees = (long)Math.Floor(amount);
        var paisa = (int)Math.Round((amount - rupees) * 100m, 0);

        if (rupees == 0 && paisa == 0)
        {
            return "Zero Rupees Only";
        }

        var words = ConvertNumber(rupees);
        var result = string.IsNullOrWhiteSpace(words) ? string.Empty : $"{words} Rupees";

        if (paisa > 0)
        {
            var paisaWords = ConvertNumber(paisa);
            result += string.IsNullOrWhiteSpace(paisaWords)
                ? string.Empty
                : $" and {paisaWords} Paisa";
        }

        return $"{result} Only".Trim();
    }

    private static string ConvertNumber(long number)
    {
        if (number == 0)
        {
            return string.Empty;
        }

        if (number < 0)
        {
            return $"Minus {ConvertNumber(Math.Abs(number))}";
        }

        var parts = new List<string>();

        AppendScale(parts, ref number, 10000000, "Crore");
        AppendScale(parts, ref number, 100000, "Lakh");
        AppendScale(parts, ref number, 1000, "Thousand");
        AppendScale(parts, ref number, 100, "Hundred");

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

    private static void AppendScale(List<string> parts, ref long number, long scale, string label)
    {
        if (number < scale)
        {
            return;
        }

        var count = number / scale;
        number %= scale;
        var words = ConvertNumber(count);
        if (!string.IsNullOrWhiteSpace(words))
        {
            parts.Add($"{words} {label}");
        }
    }
}
