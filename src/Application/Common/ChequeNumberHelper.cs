using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Common;

public static partial class ChequeNumberHelper
{
    public static string? Increment(string? chequeNumber)
    {
        if (string.IsNullOrWhiteSpace(chequeNumber))
        {
            return null;
        }

        var trimmed = chequeNumber.Trim();

        if (long.TryParse(trimmed, out var wholeNumber))
        {
            return FormatIncrementedNumber(trimmed, wholeNumber + 1);
        }

        var match = TrailingDigitsRegex().Match(trimmed);
        if (!match.Success)
        {
            return trimmed;
        }

        var suffix = match.Groups[1].Value;
        if (!long.TryParse(suffix, out var suffixNumber))
        {
            return trimmed;
        }

        var prefix = trimmed[..^suffix.Length];
        return prefix + FormatIncrementedNumber(suffix, suffixNumber + 1);
    }

    private static string FormatIncrementedNumber(string original, long nextValue)
    {
        if (original.Length > 1 && original[0] == '0')
        {
            return nextValue.ToString().PadLeft(original.Length, '0');
        }

        return nextValue.ToString();
    }

    [GeneratedRegex(@"(\d+)$")]
    private static partial Regex TrailingDigitsRegex();
}
