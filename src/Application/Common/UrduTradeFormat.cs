namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// Company 3 handwritten Urdu lot/stack style: لاٹ#120 and اسٹیک #---
/// </summary>
public static class UrduTradeFormat
{
    public const string EmptyMarks = "---";

    public static string Lot(string? lotNo)
    {
        var value = string.IsNullOrWhiteSpace(lotNo) ? EmptyMarks : lotNo.Trim();
        return $"لاٹ#{value}";
    }

    public static string Stack(string? stackNo)
    {
        var value = string.IsNullOrWhiteSpace(stackNo) ? EmptyMarks : stackNo.Trim();
        return $"اسٹیک #{value}";
    }
}
