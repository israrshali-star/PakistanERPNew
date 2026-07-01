namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// Resolves cartons for stock movement lines from bill/invoice lines,
/// with stack and item-level fallbacks when lot/stack keys do not match exactly.
/// </summary>
public sealed class MovementCartonResolver
{
    private readonly Dictionary<string, (decimal Cartons, decimal Qty)> _exact =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (decimal Cartons, decimal Qty)> _byRefItemStack =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (decimal Cartons, decimal Qty)> _byRefItem =
        new(StringComparer.OrdinalIgnoreCase);

    public void Add(
        string referenceNo,
        int itemId,
        string? stackNo,
        string? lotNo,
        decimal cartons,
        decimal quantity)
    {
        cartons = Math.Round(Math.Max(0m, cartons), 2);
        quantity = Math.Round(Math.Max(0m, quantity), 2);
        if (cartons <= 0m || quantity <= 0m)
        {
            return;
        }

        Accumulate(
            _exact,
            MovementCartonKey(referenceNo, itemId, stackNo, lotNo),
            cartons,
            quantity);
        Accumulate(_byRefItemStack, RefItemStackKey(referenceNo, itemId, stackNo), cartons, quantity);
        Accumulate(_byRefItem, RefItemKey(referenceNo, itemId), cartons, quantity);
    }

    public decimal Resolve(
        string? referenceNo,
        int itemId,
        string? stackNo,
        string? lotNo,
        decimal transactionQty)
    {
        if (string.IsNullOrWhiteSpace(referenceNo) || transactionQty <= 0m)
        {
            return 0m;
        }

        transactionQty = Math.Round(transactionQty, 2);

        if (TryProportional(
                _exact,
                MovementCartonKey(referenceNo, itemId, stackNo, lotNo),
                transactionQty,
                out var exact))
        {
            return exact;
        }

        if (TryProportional(
                _byRefItemStack,
                RefItemStackKey(referenceNo, itemId, stackNo),
                transactionQty,
                out var byStack))
        {
            return byStack;
        }

        if (TryProportional(
                _byRefItem,
                RefItemKey(referenceNo, itemId),
                transactionQty,
                out var byItem))
        {
            return byItem;
        }

        return 0m;
    }

    private static bool TryProportional(
        IReadOnlyDictionary<string, (decimal Cartons, decimal Qty)> source,
        string key,
        decimal transactionQty,
        out decimal cartons)
    {
        cartons = 0m;
        if (!source.TryGetValue(key, out var aggregate) || aggregate.Qty <= 0m || aggregate.Cartons <= 0m)
        {
            return false;
        }

        cartons = Math.Round(aggregate.Cartons * transactionQty / aggregate.Qty, 2);
        return cartons > 0m;
    }

    private static void Accumulate(
        Dictionary<string, (decimal Cartons, decimal Qty)> target,
        string key,
        decimal cartons,
        decimal quantity)
    {
        if (target.TryGetValue(key, out var existing))
        {
            target[key] = (
                Math.Round(existing.Cartons + cartons, 2),
                Math.Round(existing.Qty + quantity, 2));
            return;
        }

        target[key] = (cartons, quantity);
    }

    public static string MovementCartonKey(
        string referenceNo,
        int itemId,
        string? stackNo,
        string? lotNo) =>
        $"{referenceNo.Trim().ToUpperInvariant()}|{itemId}|{NormalizeKeyPart(stackNo)}|{NormalizeKeyPart(lotNo)}";

    public static string RefItemStackKey(string referenceNo, int itemId, string? stackNo) =>
        $"{referenceNo.Trim().ToUpperInvariant()}|{itemId}|{NormalizeKeyPart(stackNo)}";

    public static string RefItemKey(string referenceNo, int itemId) =>
        $"{referenceNo.Trim().ToUpperInvariant()}|{itemId}";

    public static string NormalizeKeyPart(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
}
