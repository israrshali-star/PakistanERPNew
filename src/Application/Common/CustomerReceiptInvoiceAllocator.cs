using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// FIFO-applies a customer receipt against opening balance and unpaid invoices
/// in chronological order (same sequence as the customer ledger).
/// </summary>
public static class CustomerReceiptInvoiceAllocator
{
    public const string OpeningBalanceReference = "Opening Balance";
    public const int ReceiptSortOffset = 1_000_000;
    public const int WriteChequeSortOffset = 2_000_000;

    public sealed record Movement(
        DateTime Date,
        int SortKey,
        bool IsReceivable,
        string Reference,
        decimal Amount,
        bool IsTargetReceipt = false);

    public static CustomerReceiptInvoiceAllocationDto Allocate(
        decimal openingBalance,
        IReadOnlyList<Movement> movements,
        decimal targetReceiptAmount,
        decimal outstandingBefore)
    {
        var unpaid = new List<UnpaidItem>();
        var applied = new List<CustomerReceiptInvoiceLineDto>();
        var creditPool = 0m;

        if (openingBalance > 0m)
        {
            unpaid.Add(new UnpaidItem(OpeningBalanceReference, null, openingBalance));
        }
        else if (openingBalance < 0m)
        {
            creditPool = Math.Abs(openingBalance);
        }

        foreach (var movement in movements.OrderBy(m => m.Date.Date).ThenBy(m => m.SortKey))
        {
            if (movement.IsTargetReceipt)
            {
                var leftover = ApplyCredit(unpaid, Math.Max(0m, targetReceiptAmount), applied);
                var remaining = outstandingBefore - targetReceiptAmount;
                return new CustomerReceiptInvoiceAllocationDto(
                    outstandingBefore,
                    remaining,
                    leftover,
                    applied);
            }

            if (movement.Amount <= 0m)
            {
                continue;
            }

            if (movement.IsReceivable)
            {
                var amount = movement.Amount;
                if (creditPool > 0m)
                {
                    var used = Math.Min(amount, creditPool);
                    amount -= used;
                    creditPool -= used;
                }

                if (amount > 0m)
                {
                    unpaid.Add(new UnpaidItem(movement.Reference, movement.Date.Date, amount));
                }
            }
            else
            {
                creditPool += ApplyCredit(unpaid, movement.Amount, recorded: null);
            }
        }

        var remainingAfter = outstandingBefore - targetReceiptAmount;
        return new CustomerReceiptInvoiceAllocationDto(
            outstandingBefore,
            remainingAfter,
            Math.Max(0m, targetReceiptAmount),
            applied);
    }

    private static decimal ApplyCredit(
        List<UnpaidItem> unpaid,
        decimal credit,
        List<CustomerReceiptInvoiceLineDto>? recorded)
    {
        for (var i = 0; i < unpaid.Count && credit > 0m; i++)
        {
            var row = unpaid[i];
            if (row.Remaining <= 0m)
            {
                continue;
            }

            var applied = Math.Min(row.Remaining, credit);
            unpaid[i] = row with { Remaining = row.Remaining - applied };
            credit -= applied;
            recorded?.Add(new CustomerReceiptInvoiceLineDto(
                row.Reference,
                row.InvoiceDate,
                applied));
        }

        unpaid.RemoveAll(item => item.Remaining <= 0m);
        return credit;
    }

    private sealed record UnpaidItem(string Reference, DateTime? InvoiceDate, decimal Remaining);
}
