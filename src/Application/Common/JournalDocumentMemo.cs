namespace PakistanAccountingERP.Application.Common;

public static class JournalDocumentMemo
{
    public static string WithDocumentNumber(string? documentNumber, string? memo)
    {
        var number = documentNumber?.Trim();
        var text = memo?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(number))
        {
            return text;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return number;
        }

        return text.Contains(number, StringComparison.OrdinalIgnoreCase)
            ? text
            : $"{number} — {text}";
    }
}
