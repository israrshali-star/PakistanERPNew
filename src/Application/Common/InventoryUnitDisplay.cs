namespace PakistanAccountingERP.Application.Common;

public static class InventoryUnitDisplay
{
    public static string Format(string? itemCode, string? unitSymbol)
    {
        var code = itemCode?.Trim() ?? string.Empty;
        if (code.StartsWith("W", StringComparison.OrdinalIgnoreCase))
        {
            return "kg";
        }

        if (code.StartsWith("C", StringComparison.OrdinalIgnoreCase))
        {
            return "Ctn";
        }

        var symbol = unitSymbol?.Trim();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return "PCS";
        }

        if (symbol.Equals("KG", StringComparison.OrdinalIgnoreCase)
            || symbol.Equals("KGS", StringComparison.OrdinalIgnoreCase))
        {
            return "kg";
        }

        if (symbol.Equals("CTN", StringComparison.OrdinalIgnoreCase)
            || symbol.Equals("CARTON", StringComparison.OrdinalIgnoreCase))
        {
            return "Ctn";
        }

        if (symbol.Equals("LB", StringComparison.OrdinalIgnoreCase)
            || symbol.Equals("POUND", StringComparison.OrdinalIgnoreCase))
        {
            return "kg";
        }

        return symbol;
    }
}
