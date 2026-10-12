using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

/// <summary>
/// One-time correction for INV-0347 stack 1158551: 3 cartons were posted at 72.50 instead of 70.92.
/// Running again does nothing once that line is already 70.92.
/// </summary>
public class Invoice0347WeightCorrection
{
    public const string InvoiceSuffix = "0347";
    public const string StackNo = "1158551";
    public const decimal OldWeight = 72.50m;
    public const decimal NewWeight = 70.92m;
    public const decimal Cartons = 3m;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<Invoice0347WeightCorrection> _logger;

    public Invoice0347WeightCorrection(
        IUnitOfWork unitOfWork,
        ILogger<Invoice0347WeightCorrection> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var invoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query(asNoTracking: false)
            .Include(i => i.Lines)
            .Where(i => i.InvoiceNumber.EndsWith(InvoiceSuffix) || i.InvoiceNumber.EndsWith("-347"))
            .ToListAsync(cancellationToken);

        var matches = invoices.Where(i => IsInvoice0347(i.InvoiceNumber)).ToList();
        if (matches.Count == 0)
        {
            _logger.LogWarning("INV-0347 was not found, so stack {StackNo} weight was not changed.", StackNo);
            return;
        }

        foreach (var invoice in matches)
        {
            try
            {
                await CorrectInvoiceAsync(invoice, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not correct weight on {InvoiceNumber}.", invoice.InvoiceNumber);
            }
        }
    }

    private async Task CorrectInvoiceAsync(SalesInvoice invoice, CancellationToken cancellationToken)
    {
        var line = invoice.Lines
            .Where(l => string.Equals(l.StackNo?.Trim(), StackNo, StringComparison.Ordinal))
            .Where(l => Math.Abs(l.Quantity - OldWeight) < 0.001m)
            .OrderByDescending(l => Math.Abs(l.Cartons - Cartons) < 0.001m)
            .FirstOrDefault();

        if (line is null)
        {
            var already = invoice.Lines.Any(l =>
                string.Equals(l.StackNo?.Trim(), StackNo, StringComparison.Ordinal)
                && Math.Abs(l.Quantity - NewWeight) < 0.001m);
            if (already)
            {
                _logger.LogInformation(
                    "{InvoiceNumber} stack {StackNo} is already {Weight:N2}.",
                    invoice.InvoiceNumber,
                    StackNo,
                    NewWeight);
                return;
            }

            _logger.LogWarning(
                "{InvoiceNumber} has no stack {StackNo} line at weight {OldWeight:N2}. Lines: {Lines}",
                invoice.InvoiceNumber,
                StackNo,
                OldWeight,
                string.Join("; ", invoice.Lines.Select(l => $"stack {l.StackNo} qty {l.Quantity:N2} ctn {l.Cartons:N2}")));
            return;
        }

        var itemIds = invoice.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await _unitOfWork.Repository<Item>()
            .Query(asNoTracking: false)
            .Where(i => i.CompanyId == invoice.CompanyId && itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        var oldQuantity = line.Quantity;
        var oldSubTotal = Math.Round(oldQuantity * line.Price, 2);
        var newSubTotal = Math.Round(NewWeight * line.Price, 2);
        var oldTaxable = SalesTaxSplit.ComputeLineTaxable(oldQuantity, line.Price, line.Discount);
        var newTaxable = SalesTaxSplit.ComputeLineTaxable(NewWeight, line.Price, line.Discount);
        var taxableDelta = newTaxable - oldTaxable;
        var oldLineTax = line.TaxAmount;
        var oldLineTotal = line.LineTotal;
        var oldProductDescription = line.ProductDescription;
        var oldCartonDescription = line.CartonDescription;
        var oldGoods = invoice.Lines
            .Where(l => IsGoodsLine(l, items))
            .Sum(l => SalesTaxSplit.ComputeLineTaxable(l.Quantity, l.Price, l.Discount));

        decimal newSalesTax = invoice.TaxAmount;
        decimal newFurtherTax = invoice.FurtherTax;
        if (IsGoodsLine(line, items) && oldGoods > 0m)
        {
            var newGoods = oldGoods + taxableDelta;
            newSalesTax = Math.Round(invoice.TaxAmount * newGoods / oldGoods, 2);
            newFurtherTax = Math.Round(invoice.FurtherTax * newGoods / oldGoods, 2);
        }

        var salesTaxDelta = newSalesTax - invoice.TaxAmount;
        var furtherTaxDelta = newFurtherTax - invoice.FurtherTax;
        var netDelta = taxableDelta + salesTaxDelta + furtherTaxDelta;

        if (oldLineTax != 0m && oldTaxable != 0m)
        {
            line.TaxAmount = Math.Round(oldLineTax * newTaxable / oldTaxable, 2);
        }

        if (Math.Abs(line.LineTotal - Math.Round(oldTaxable + oldLineTax, 2)) <= 0.02m)
        {
            line.LineTotal = Math.Round(newTaxable + line.TaxAmount, 2);
        }
        else
        {
            line.LineTotal = Math.Round(line.LineTotal + taxableDelta + (line.TaxAmount - oldLineTax), 2);
        }

        line.Quantity = NewWeight;
        line.ProductDescription = ReplaceWeight(line.ProductDescription);
        line.CartonDescription = ReplaceWeight(line.CartonDescription);

        var oldInvoiceSubTotal = invoice.SubTotal;
        var oldInvoiceTax = invoice.TaxAmount;
        var oldInvoiceFurther = invoice.FurtherTax;
        var oldInvoiceNet = invoice.NetTotal;

        invoice.SubTotal = Math.Round(invoice.SubTotal + (newSubTotal - oldSubTotal), 2);
        invoice.TaxAmount = newSalesTax;
        invoice.FurtherTax = newFurtherTax;
        invoice.NetTotal = Math.Round(invoice.NetTotal + netDelta, 2);
        invoice.UpdatedAt = DateTime.UtcNow;
        invoice.UpdatedBy = "weight-correction";

        if (invoice.Status == InvoiceStatus.Posted)
        {
            var costDelta = await PreviewCostDeltaAsync(invoice, line, oldQuantity, cancellationToken);
            var journalOk = await CorrectJournalAsync(
                invoice,
                taxableDelta,
                salesTaxDelta,
                furtherTaxDelta,
                netDelta,
                costDelta,
                cancellationToken);
            if (!journalOk)
            {
                RestoreInvoice(
                    invoice,
                    line,
                    oldQuantity,
                    oldLineTax,
                    oldLineTotal,
                    oldProductDescription,
                    oldCartonDescription,
                    oldInvoiceSubTotal,
                    oldInvoiceTax,
                    oldInvoiceFurther,
                    oldInvoiceNet);
                _logger.LogError(
                    "Left {InvoiceNumber} unchanged because its journal entry would not balance after the weight change.",
                    invoice.InvoiceNumber);
                return;
            }

            await ApplyInventoryAsync(invoice, line, items, oldQuantity, cancellationToken);
        }

        await _unitOfWork.Repository<AuditLog>().AddAsync(new AuditLog
        {
            Action = "CorrectWeight",
            TableName = "SalesInvoiceLines",
            RecordId = line.Id.ToString(),
            OldValue = $"{invoice.InvoiceNumber} stack {StackNo} weight {OldWeight:N2}",
            NewValue = $"{invoice.InvoiceNumber} stack {StackNo} weight {NewWeight:N2}",
            UserName = "weight-correction",
            CreatedAt = DateTime.UtcNow,
            CompanyId = invoice.CompanyId
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Corrected {InvoiceNumber} stack {StackNo} weight from {OldWeight:N2} to {NewWeight:N2}. Net total is now {NetTotal:N2}.",
            invoice.InvoiceNumber,
            StackNo,
            OldWeight,
            NewWeight,
            invoice.NetTotal);
    }

    private async Task<List<InventoryTransaction>> MatchingMovementsAsync(
        SalesInvoice invoice,
        SalesInvoiceLine line,
        decimal oldQuantity,
        bool track,
        CancellationToken cancellationToken)
    {
        var stack = line.StackNo?.Trim();
        var transactions = await _unitOfWork.Repository<InventoryTransaction>()
            .Query(asNoTracking: !track)
            .Where(t => t.CompanyId == invoice.CompanyId
                && !t.IsDeleted
                && t.ReferenceNo == invoice.InvoiceNumber
                && t.ItemId == line.ItemId)
            .ToListAsync(cancellationToken);

        return transactions
            .Where(t => string.Equals(t.StackNo?.Trim(), stack, StringComparison.Ordinal)
                && Math.Abs(t.Quantity - oldQuantity) < 0.001m)
            .ToList();
    }

    private async Task<decimal> PreviewCostDeltaAsync(
        SalesInvoice invoice,
        SalesInvoiceLine line,
        decimal oldQuantity,
        CancellationToken cancellationToken)
    {
        var matches = await MatchingMovementsAsync(invoice, line, oldQuantity, track: false, cancellationToken);
        if (matches.Count == 0)
        {
            _logger.LogWarning(
                "{InvoiceNumber} stack {StackNo} has no stock movement at {OldWeight:N2}. Invoice weight will still change.",
                invoice.InvoiceNumber,
                StackNo,
                OldWeight);
        }

        return Math.Round(matches.Sum(t => Math.Round(t.UnitCost * NewWeight, 2) - t.TotalCost), 2);
    }

    private async Task ApplyInventoryAsync(
        SalesInvoice invoice,
        SalesInvoiceLine line,
        IReadOnlyDictionary<int, Item> items,
        decimal oldQuantity,
        CancellationToken cancellationToken)
    {
        var matches = await MatchingMovementsAsync(invoice, line, oldQuantity, track: true, cancellationToken);
        if (matches.Count == 0)
        {
            return;
        }

        if (!items.TryGetValue(line.ItemId, out var item))
        {
            _logger.LogWarning(
                "{InvoiceNumber} item {ItemId} was not found, so on-hand stock was not changed.",
                invoice.InvoiceNumber,
                line.ItemId);
        }

        var now = DateTime.UtcNow;
        foreach (var transaction in matches)
        {
            var weightReduction = oldQuantity - NewWeight;
            if (item is not null && transaction.TransactionType == InventoryTransactionType.StockOut)
            {
                item.CurrentStock = Math.Round(item.CurrentStock + weightReduction, 2);
                item.UpdatedAt = now;
                item.UpdatedBy = "weight-correction";
            }
            else if (item is not null && transaction.TransactionType == InventoryTransactionType.StockIn)
            {
                item.CurrentStock = Math.Round(item.CurrentStock - weightReduction, 2);
                item.UpdatedAt = now;
                item.UpdatedBy = "weight-correction";
            }

            transaction.Quantity = NewWeight;
            transaction.TotalCost = Math.Round(transaction.UnitCost * NewWeight, 2);
            transaction.UpdatedAt = now;
            transaction.UpdatedBy = "weight-correction";
        }
    }

    private async Task<bool> CorrectJournalAsync(
        SalesInvoice invoice,
        decimal taxableDelta,
        decimal salesTaxDelta,
        decimal furtherTaxDelta,
        decimal netDelta,
        decimal costDelta,
        CancellationToken cancellationToken)
    {
        if (invoice.JournalEntryId is null)
        {
            _logger.LogWarning("{InvoiceNumber} is posted but has no journal entry. The invoice weight will still change.", invoice.InvoiceNumber);
            return true;
        }

        var journalLines = await _unitOfWork.Repository<JournalEntryLine>()
            .Query(asNoTracking: false)
            .Where(l => l.JournalEntryId == invoice.JournalEntryId)
            .ToListAsync(cancellationToken);
        var journalSnapshot = journalLines.ToDictionary(l => l.Id, l => (l.Debit, l.Credit));

        if (journalLines.Count == 0)
        {
            _logger.LogWarning("{InvoiceNumber} journal {JournalEntryId} has no lines.", invoice.InvoiceNumber, invoice.JournalEntryId);
            return false;
        }

        var receivable = FindMemo(journalLines, "Accounts Receivable");
        var revenue = FindMemo(journalLines, "Sales Revenue") ?? FindMemo(journalLines, "Sales Returns");
        var furtherTax = FindMemo(journalLines, "Further Tax");
        var salesTax = journalLines.FirstOrDefault(l =>
            ContainsMemo(l, "Sales Tax") && (furtherTax is null || l.Id != furtherTax.Id));
        var cogs = FindMemo(journalLines, "Cost of Goods Sold");
        var inventory = FindMemo(journalLines, "Inventory Asset");

        if (netDelta != 0m && receivable is null)
        {
            return false;
        }

        if (taxableDelta != 0m && revenue is null)
        {
            return false;
        }

        if ((salesTaxDelta != 0m || furtherTaxDelta != 0m) && salesTax is null && furtherTax is null)
        {
            return false;
        }

        if (costDelta != 0m && (cogs is null || inventory is null))
        {
            return false;
        }

        if (receivable is not null)
        {
            AddToPostedSide(receivable, netDelta);
        }

        if (revenue is not null)
        {
            AddToPostedSide(revenue, taxableDelta);
        }

        if (furtherTax is null)
        {
            if (salesTax is not null)
            {
                AddToPostedSide(salesTax, salesTaxDelta + furtherTaxDelta);
            }
        }
        else
        {
            if (salesTax is not null)
            {
                AddToPostedSide(salesTax, salesTaxDelta);
            }

            AddToPostedSide(furtherTax, furtherTaxDelta);
        }

        if (cogs is not null && inventory is not null && costDelta != 0m)
        {
            AddToPostedSide(cogs, costDelta);
            AddToPostedSide(inventory, costDelta);
        }

        var gap = Math.Round(journalLines.Sum(l => l.Debit) - journalLines.Sum(l => l.Credit), 2);
        if (gap != 0m && receivable is not null && Math.Abs(gap) <= 0.05m)
        {
            AddToPostedSide(receivable, -gap);
            invoice.NetTotal = Math.Round(invoice.NetTotal - gap, 2);
            gap = Math.Round(journalLines.Sum(l => l.Debit) - journalLines.Sum(l => l.Credit), 2);
        }

        var balanced = gap == 0m && journalLines.All(l => l.Debit >= 0m && l.Credit >= 0m);
        if (!balanced)
        {
            foreach (var journalLine in journalLines)
            {
                var original = journalSnapshot[journalLine.Id];
                journalLine.Debit = original.Debit;
                journalLine.Credit = original.Credit;
            }
        }

        return balanced;
    }

    private static void RestoreInvoice(
        SalesInvoice invoice,
        SalesInvoiceLine line,
        decimal oldQuantity,
        decimal oldLineTax,
        decimal oldLineTotal,
        string? oldProductDescription,
        string? oldCartonDescription,
        decimal oldSubTotal,
        decimal oldTax,
        decimal oldFurtherTax,
        decimal oldNet)
    {
        line.Quantity = oldQuantity;
        line.TaxAmount = oldLineTax;
        line.LineTotal = oldLineTotal;
        line.ProductDescription = oldProductDescription;
        line.CartonDescription = oldCartonDescription;
        invoice.SubTotal = oldSubTotal;
        invoice.TaxAmount = oldTax;
        invoice.FurtherTax = oldFurtherTax;
        invoice.NetTotal = oldNet;
    }

    private static bool IsInvoice0347(string invoiceNumber)
    {
        var trimmed = invoiceNumber.Trim();
        var dash = trimmed.LastIndexOf('-');
        var suffix = dash >= 0 ? trimmed[(dash + 1)..] : trimmed;
        return int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            && number == 347;
    }

    private static bool IsGoodsLine(SalesInvoiceLine line, IReadOnlyDictionary<int, Item> items) =>
        items.TryGetValue(line.ItemId, out var item)
        && !SalesTaxSplit.IsCartageOrService(item.ItemType, item.ItemCode);

    private static JournalEntryLine? FindMemo(IEnumerable<JournalEntryLine> lines, string token) =>
        lines.FirstOrDefault(l => ContainsMemo(l, token));

    private static bool ContainsMemo(JournalEntryLine line, string token) =>
        (line.Memo ?? string.Empty).Contains(token, StringComparison.OrdinalIgnoreCase);

    private static void AddToPostedSide(JournalEntryLine line, decimal delta)
    {
        if (delta == 0m)
        {
            return;
        }

        if (line.Debit != 0m && line.Credit == 0m)
        {
            line.Debit = Math.Round(line.Debit + delta, 2);
            return;
        }

        if (line.Credit != 0m && line.Debit == 0m)
        {
            line.Credit = Math.Round(line.Credit + delta, 2);
        }
    }

    private static string? ReplaceWeight(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var updated = System.Text.RegularExpressions.Regex.Replace(text, @"\b72\.50\b", "70.92");
        return System.Text.RegularExpressions.Regex.Replace(updated, @"\b72\.5\b", "70.92");
    }
}
