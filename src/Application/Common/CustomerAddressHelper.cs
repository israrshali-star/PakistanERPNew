namespace PakistanAccountingERP.Application.Common;

public static class CustomerAddressHelper
{
    public static string? BuildFromParts(string? buyerName, params string?[] parts)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            var trimmed = part.Trim();
            if (seen.Contains(trimmed))
            {
                continue;
            }

            if (values.Count == 0
                && !string.IsNullOrWhiteSpace(buyerName)
                && string.Equals(trimmed, buyerName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            seen.Add(trimmed);
            values.Add(trimmed);
        }

        return values.Count == 0 ? null : string.Join(", ", values);
    }

    public static string? RemoveLeadingBuyerName(string? buyerName, string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var trimmedAddress = address.Trim();
        if (string.IsNullOrWhiteSpace(buyerName))
        {
            return trimmedAddress;
        }

        var name = buyerName.Trim();
        if (string.Equals(trimmedAddress, name, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!trimmedAddress.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            return trimmedAddress;
        }

        var remainder = trimmedAddress[name.Length..].TrimStart();
        if (remainder.StartsWith(','))
        {
            remainder = remainder[1..].TrimStart();
        }

        return string.IsNullOrWhiteSpace(remainder) ? null : remainder;
    }
}
