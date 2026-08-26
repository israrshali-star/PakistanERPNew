using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// FIFO-applies vendor payments (and write-cheque withdrawals) against opening balance
/// and unpaid vendor bills in chronological order (same sequence as the vendor ledger).
/// Report lines are placed in the bill month: a later payment is pulled back into the
/// bill month, and an earlier advance is pushed forward into the later bill month.
/// </summary>
public static class VendorPaymentBillAllocator
{
    public const string OpeningBalanceReference = "Opening Balance";
    public const string OpeningAdjustmentNumber = "OPENING";
    public const string OpeningAdjustmentSource = "Opening Adjustment";
    public const int PaymentSortOffset = 1_000_000;
    public const int WriteChequeSortOffset = 2_000_000;

    public sealed record PayableMovement(
        DateTime Date,
        int SortKey,
        string RefNo,
        string BillNumber,
        DateTime? BillDate,
        decimal Amount);

    public sealed record PaymentMovement(
        int Id,
        DateTime Date,
        int SortKey,
        string PaymentNumber,
        int VendorId,
        string VendorName,
        string Source,
        string PaymentMethod,
        string? BankName,
        string? ChequeNumber,
        decimal Amount);

    public sealed record AllocationResult(
        IReadOnlyList<VendorPaymentMonthlyLineDto> Payments,
        IReadOnlyList<VendorPaymentAppliedRefDto> OpeningAppliedInRange);

    public static AllocationResult Allocate(
        decimal openingBalance,
        IReadOnlyList<PayableMovement> payables,
        IReadOnlyList<PaymentMovement> payments,
        DateTime fromInclusive,
        DateTime toInclusive)
    {
        var unpaid = new List<UnpaidItem>();
        var advances = new List<AdvanceSlot>();
        var openingCollector = new OpeningAdvanceCollector();

        if (openingBalance > 0m)
        {
            unpaid.Add(new UnpaidItem(
                OpeningBalanceReference,
                "OPENING",
                null,
                openingBalance));
        }
        else if (openingBalance < 0m)
        {
            advances.Add(new AdvanceSlot(null, openingCollector, Math.Abs(openingBalance)));
        }

        var events = new List<(DateTime Date, int SortKey, bool IsPayment, PayableMovement? Payable, PaymentMovement? Payment)>(
            payables.Count + payments.Count);

        foreach (var payable in payables)
        {
            if (payable.Amount <= 0m)
            {
                continue;
            }

            events.Add((payable.Date.Date, payable.SortKey, false, payable, null));
        }

        foreach (var payment in payments)
        {
            if (payment.Amount <= 0m)
            {
                continue;
            }

            events.Add((payment.Date.Date, payment.SortKey, true, null, payment));
        }

        var from = fromInclusive.Date;
        var to = toInclusive.Date;
        var allResults = new List<PaymentResult>();

        foreach (var ev in events.OrderBy(e => e.Date).ThenBy(e => e.SortKey))
        {
            if (!ev.IsPayment)
            {
                ApplyBillToAdvancesThenUnpaid(unpaid, advances, ev.Payable!, from, to);
                continue;
            }

            var payment = ev.Payment!;
            var applied = new List<VendorPaymentAppliedRefDto>();
            var leftover = ApplyCredit(unpaid, payment.Amount, applied);
            var result = new PaymentResult(payment, applied, leftover);
            allResults.Add(result);

            if (leftover > 0m)
            {
                advances.Add(new AdvanceSlot(result, null, leftover));
            }
        }

        return new AllocationResult(
            BuildPlacedPaymentLines(allResults, from, to),
            openingCollector.AppliedInRange);
    }

    public static IReadOnlyList<VendorPaymentMonthlyLineDto> BuildOpeningAdjustmentLines(
        int vendorId,
        string vendorName,
        IReadOnlyList<VendorPaymentAppliedRefDto> openingAppliedInRange)
    {
        if (openingAppliedInRange.Count == 0)
        {
            return [];
        }

        return openingAppliedInRange
            .GroupBy(r =>
            {
                var date = r.BillDate?.Date ?? DateTime.MinValue;
                return new { date.Year, date.Month };
            })
            .OrderBy(g => g.Key.Year)
            .ThenBy(g => g.Key.Month)
            .Select(g =>
            {
                var refs = g.ToList();
                var date = refs.Min(r => r.BillDate ?? DateTime.MinValue);
                var amount = refs.Sum(r => r.AppliedAmount);
                return new VendorPaymentMonthlyLineDto(
                    -vendorId,
                    OpeningAdjustmentNumber,
                    date,
                    vendorId,
                    vendorName,
                    OpeningAdjustmentSource,
                    OpeningBalanceReference,
                    null,
                    null,
                    amount,
                    0m,
                    refs);
            })
            .Where(line => line.Amount >= 1m)
            .ToList();
    }

    private static IReadOnlyList<VendorPaymentMonthlyLineDto> BuildPlacedPaymentLines(
        IReadOnlyList<PaymentResult> payments,
        DateTime fromInclusive,
        DateTime toInclusive)
    {
        var from = fromInclusive.Date;
        var to = toInclusive.Date;
        var lines = new List<VendorPaymentMonthlyLineDto>();

        foreach (var result in payments)
        {
            var payment = result.Payment;
            var paymentDate = payment.Date.Date;
            var paymentMonth = MonthKey(paymentDate);
            var openingRefs = new List<VendorPaymentAppliedRefDto>();
            var refsByMonth = new Dictionary<(int Year, int Month), List<VendorPaymentAppliedRefDto>>();

            foreach (var applied in result.Applied)
            {
                if (applied.AppliedAmount <= 0m)
                {
                    continue;
                }

                if (string.Equals(applied.RefNo, OpeningBalanceReference, StringComparison.OrdinalIgnoreCase)
                    || applied.BillDate is null)
                {
                    openingRefs.Add(applied);
                    continue;
                }

                var billDate = applied.BillDate.Value.Date;
                var key = (billDate.Year, billDate.Month);
                if (!refsByMonth.TryGetValue(key, out var monthRefs))
                {
                    monthRefs = [];
                    refsByMonth[key] = monthRefs;
                }

                monthRefs.Add(applied);
            }

            var unallocatedPlaced = false;
            foreach (var monthRefs in refsByMonth.Values.OrderBy(r => r.Min(x => x.BillDate)))
            {
                var reportDate = monthRefs.Min(r => r.BillDate!.Value.Date);
                var amount = monthRefs.Sum(r => r.AppliedAmount);
                var unallocated = 0m;
                if (MonthKey(reportDate) == paymentMonth)
                {
                    amount += result.Unallocated;
                    unallocated = result.Unallocated;
                    unallocatedPlaced = true;
                }

                if (amount < 1m || reportDate < from || reportDate > to)
                {
                    continue;
                }

                lines.Add(CreatePaymentLine(
                    payment,
                    paymentDate,
                    amount,
                    unallocated,
                    monthRefs,
                    reportDate));
            }

            var residualRefs = openingRefs;
            var residualAmount = residualRefs.Sum(r => r.AppliedAmount)
                + (unallocatedPlaced ? 0m : result.Unallocated);
            var residualUnallocated = unallocatedPlaced ? 0m : result.Unallocated;
            if (paymentDate >= from && paymentDate <= to && residualAmount >= 1m)
            {
                lines.Add(CreatePaymentLine(
                    payment,
                    paymentDate,
                    residualAmount,
                    residualUnallocated,
                    residualRefs,
                    null));
            }
        }

        return lines;
    }

    private static VendorPaymentMonthlyLineDto CreatePaymentLine(
        PaymentMovement payment,
        DateTime paymentDate,
        decimal amount,
        decimal unallocated,
        IReadOnlyList<VendorPaymentAppliedRefDto> refs,
        DateTime? reportDate) =>
        new(
            payment.Id,
            payment.PaymentNumber,
            paymentDate,
            payment.VendorId,
            payment.VendorName,
            payment.Source,
            payment.PaymentMethod,
            payment.BankName,
            payment.ChequeNumber,
            amount,
            unallocated,
            refs,
            ReportDate: reportDate);

    private static int MonthKey(DateTime date) => date.Year * 12 + date.Month;

    public static string ResolveBillRef(string? refNo, string billNumber)
    {
        if (!string.IsNullOrWhiteSpace(refNo))
        {
            return refNo.Trim();
        }

        return billNumber;
    }

    private static void ApplyBillToAdvancesThenUnpaid(
        List<UnpaidItem> unpaid,
        List<AdvanceSlot> advances,
        PayableMovement payable,
        DateTime from,
        DateTime to)
    {
        var amount = payable.Amount;
        var billDate = payable.BillDate?.Date ?? payable.Date.Date;
        var inRange = billDate >= from && billDate <= to;

        for (var i = 0; i < advances.Count && amount > 0m; i++)
        {
            var slot = advances[i];
            if (slot.Remaining <= 0m)
            {
                continue;
            }

            var used = Math.Min(slot.Remaining, amount);
            slot.Remaining -= used;
            amount -= used;

            if (slot.Opening is not null && inRange)
            {
                slot.Opening.AppliedInRange.Add(new VendorPaymentAppliedRefDto(
                    payable.RefNo,
                    payable.BillNumber,
                    payable.BillDate,
                    used));
            }

            if (slot.Result is not null)
            {
                slot.Result.Applied.Add(new VendorPaymentAppliedRefDto(
                    payable.RefNo,
                    payable.BillNumber,
                    payable.BillDate,
                    used));
                slot.Result.Unallocated = Math.Max(0m, slot.Result.Unallocated - used);
            }
        }

        advances.RemoveAll(slot => slot.Remaining <= 0m);

        if (amount > 0m)
        {
            unpaid.Add(new UnpaidItem(
                payable.RefNo,
                payable.BillNumber,
                payable.BillDate,
                amount));
        }
    }

    private static decimal ApplyCredit(
        List<UnpaidItem> unpaid,
        decimal credit,
        List<VendorPaymentAppliedRefDto> recorded)
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
            recorded.Add(new VendorPaymentAppliedRefDto(
                row.RefNo,
                row.BillNumber,
                row.BillDate,
                applied));
        }

        unpaid.RemoveAll(item => item.Remaining <= 0m);
        return credit;
    }

    private sealed record UnpaidItem(
        string RefNo,
        string BillNumber,
        DateTime? BillDate,
        decimal Remaining);

    private sealed class AdvanceSlot(PaymentResult? result, OpeningAdvanceCollector? opening, decimal remaining)
    {
        public PaymentResult? Result { get; } = result;
        public OpeningAdvanceCollector? Opening { get; } = opening;
        public decimal Remaining { get; set; } = remaining;
    }

    private sealed class OpeningAdvanceCollector
    {
        public List<VendorPaymentAppliedRefDto> AppliedInRange { get; } = [];
    }

    private sealed class PaymentResult(
        PaymentMovement payment,
        List<VendorPaymentAppliedRefDto> applied,
        decimal unallocated)
    {
        public PaymentMovement Payment { get; } = payment;
        public List<VendorPaymentAppliedRefDto> Applied { get; } = applied;
        public decimal Unallocated { get; set; } = unallocated;
    }
}
