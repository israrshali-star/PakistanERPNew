using System.Globalization;
using System.Text;

namespace PakistanAccountingERP.Application.Import;

public sealed class QuickBooksIifDocument
{
    public List<IifRecord> Accounts { get; } = [];
    public List<IifRecord> InvItems { get; } = [];
    public List<IifRecord> Customers { get; } = [];
    public List<IifRecord> Vendors { get; } = [];
    public List<IifTransactionBlock> Transactions { get; } = [];
}

public sealed class IifTransactionBlock
{
    public required IifRecord Trns { get; init; }
    public List<IifRecord> Splits { get; } = [];
}

public sealed class IifRecord
{
    public required string RecordType { get; init; }
    public required IReadOnlyDictionary<string, string> Fields { get; init; }

    public string Get(string key) =>
        Fields.TryGetValue(key, out var value) ? value : string.Empty;
}

public static class QuickBooksIifParser
{
    private static readonly HashSet<string> SectionTerminators =
    [
        "ENDTAX",
        "ENDGRP",
        "ENDCUSTITEMDICT",
        "ENDCUSTNAMEDICT"
    ];

    public static QuickBooksIifDocument Parse(string filePath)
    {
        var document = new QuickBooksIifDocument();
        string[]? headers = null;
        string[]? trnsHeaders = null;
        string[]? splHeaders = null;
        IifTransactionBlock? currentTransaction = null;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            if (rawLine.StartsWith('!'))
            {
                var headerFields = ParseLine(rawLine);
                if (headerFields.Length == 0 || headerFields[0].StartsWith("!END", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var section = headerFields[0];
                if (section.Equals("!TRNS", StringComparison.OrdinalIgnoreCase))
                {
                    trnsHeaders = headerFields.Skip(1).ToArray();
                    continue;
                }

                if (section.Equals("!SPL", StringComparison.OrdinalIgnoreCase))
                {
                    splHeaders = headerFields.Skip(1).ToArray();
                    continue;
                }

                headers = headerFields.Skip(1).ToArray();
                continue;
            }

            var fields = ParseLine(rawLine);
            if (fields.Length == 0)
            {
                continue;
            }

            var recordType = fields[0];
            if (SectionTerminators.Contains(recordType))
            {
                continue;
            }

            if (recordType.Equals("ENDTRNS", StringComparison.OrdinalIgnoreCase))
            {
                if (currentTransaction is not null)
                {
                    document.Transactions.Add(currentTransaction);
                    currentTransaction = null;
                }

                continue;
            }

            if (recordType.Equals("TRNS", StringComparison.OrdinalIgnoreCase) && trnsHeaders is not null)
            {
                if (currentTransaction is not null)
                {
                    document.Transactions.Add(currentTransaction);
                }

                currentTransaction = new IifTransactionBlock
                {
                    Trns = CreateRecord(recordType, trnsHeaders, fields)
                };
                continue;
            }

            if (recordType.Equals("SPL", StringComparison.OrdinalIgnoreCase) && splHeaders is not null && currentTransaction is not null)
            {
                currentTransaction.Splits.Add(CreateRecord(recordType, splHeaders, fields));
                continue;
            }

            if (headers is null || fields.Length < 2)
            {
                continue;
            }

            var record = CreateRecord(recordType, headers, fields);

            switch (recordType)
            {
                case "ACCNT":
                    document.Accounts.Add(record);
                    break;
                case "INVITEM" when headers.Contains("ACCNT", StringComparer.OrdinalIgnoreCase)
                                    || headers.Contains("ASSETACCNT", StringComparer.OrdinalIgnoreCase):
                    document.InvItems.Add(record);
                    break;
                case "CUST":
                    document.Customers.Add(record);
                    break;
                case "VEND":
                    document.Vendors.Add(record);
                    break;
            }
        }

        if (currentTransaction is not null)
        {
            document.Transactions.Add(currentTransaction);
        }

        return document;
    }

    private static IifRecord CreateRecord(string recordType, string[] headers, string[] fields)
    {
        var recordFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fieldCount = Math.Min(headers.Length, fields.Length - 1);
        for (var i = 0; i < fieldCount; i++)
        {
            recordFields[headers[i]] = fields[i + 1];
        }

        return new IifRecord
        {
            RecordType = recordType,
            Fields = recordFields
        };
    }

    public static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = new[]
        {
            "MM/dd/yyyy",
            "M/d/yyyy",
            "dd/MM/yyyy",
            "d/M/yyyy",
            "yyyy-MM-dd"
        };

        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact.Date;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Date
            : null;
    }

    public static decimal ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        var normalized = value.Trim().Trim('"').Replace(",", string.Empty);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0m;
    }

    public static string[] ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (c == '\t' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
