using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Import;

internal static partial class QuickBooksIifImportSupport
{
    private const string ImportUser = "quickbooks-iif-import";

    public static async Task<int> GetOrCreateImportItemIdAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existingId = await unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.ItemCode == "QB-IMPORT")
            .Select(i => (int?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingId.HasValue)
        {
            return existingId.Value;
        }

        var item = new Item
        {
            CompanyId = companyId,
            ItemType = ItemType.Service,
            ItemCode = "QB-IMPORT",
            ItemName = "QuickBooks Import",
            StackNo = "QB-IMPORT",
            LotNo = "QB-IMPORT",
            Description = "Placeholder line for QuickBooks imported documents",
            UnitOfMeasureId = 3,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = ImportUser
        };

        await unitOfWork.Repository<Item>().AddAsync(item, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return item.Id;
    }

    public static async Task<(bool Success, string? Message)> PostCustomerOpeningBalanceAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        int customerId,
        string buyerName,
        decimal openingBalance,
        DateTime entryDate,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await RemoveJournalByReferenceAsync(
            unitOfWork,
            companyId,
            ReferenceTypes.Customer,
            customerId,
            cancellationToken);

        if (openingBalance == 0m)
        {
            return (true, null);
        }

        var arAccountId = await QuickBooksImportAccountResolver.FindAccountsReceivableAsync(unitOfWork, companyId, cancellationToken);
        var equityAccountId = await QuickBooksImportAccountResolver.FindOpeningBalanceEquityAsync(unitOfWork, companyId, cancellationToken);

        if (!arAccountId.HasValue || !equityAccountId.HasValue)
        {
            return (false, "Could not resolve AR or opening balance equity accounts.");
        }

        var amount = Math.Abs(Math.Round(openingBalance, 2));
        List<JournalEntryLine> lines = openingBalance > 0m
            ?
            [
                CreateJournalLine(arAccountId.Value, amount, 0m, "Accounts Receivable"),
                CreateJournalLine(equityAccountId.Value, 0m, amount, "Opening balance offset")
            ]
            :
            [
                CreateJournalLine(equityAccountId.Value, amount, 0m, "Opening balance offset"),
                CreateJournalLine(arAccountId.Value, 0m, amount, "Accounts Receivable")
            ];

        return await CreatePostedJournalAsync(
            unitOfWork,
            companyId,
            entryDate.Date,
            $"Customer opening balance — {buyerName}",
            ReferenceTypes.Customer,
            customerId,
            lines,
            now,
            cancellationToken);
    }

    public static async Task<(bool Success, string? Message)> PostVendorOpeningBalanceAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        int vendorId,
        string vendorName,
        decimal openingBalance,
        DateTime entryDate,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await RemoveJournalByReferenceAsync(
            unitOfWork,
            companyId,
            ReferenceTypes.Vendor,
            vendorId,
            cancellationToken);

        if (openingBalance == 0m)
        {
            return (true, null);
        }

        var apAccountId = await QuickBooksImportAccountResolver.FindAccountsPayableAsync(unitOfWork, companyId, cancellationToken);
        var equityAccountId = await QuickBooksImportAccountResolver.FindOpeningBalanceEquityAsync(unitOfWork, companyId, cancellationToken);

        if (!apAccountId.HasValue || !equityAccountId.HasValue)
        {
            return (false, "Could not resolve AP or opening balance equity accounts.");
        }

        var amount = Math.Abs(Math.Round(openingBalance, 2));
        List<JournalEntryLine> lines = openingBalance > 0m
            ?
            [
                CreateJournalLine(apAccountId.Value, amount, 0m, "Accounts Payable"),
                CreateJournalLine(equityAccountId.Value, 0m, amount, "Opening balance offset")
            ]
            :
            [
                CreateJournalLine(equityAccountId.Value, amount, 0m, "Opening balance offset"),
                CreateJournalLine(apAccountId.Value, 0m, amount, "Accounts Payable")
            ];

        return await CreatePostedJournalAsync(
            unitOfWork,
            companyId,
            entryDate.Date,
            $"Vendor opening balance — {vendorName}",
            ReferenceTypes.Vendor,
            vendorId,
            lines,
            now,
            cancellationToken);
    }

    public static async Task<(bool Success, string? Message, int? InvoiceId)> CreatePostedSalesInvoiceAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        int customerId,
        string invoiceNumber,
        DateTime invoiceDate,
        decimal netTotal,
        int importItemId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (netTotal <= 0m)
        {
            return (false, "Invoice amount must be greater than zero.", null);
        }

        var duplicate = await unitOfWork.Repository<SalesInvoice>()
            .Query()
            .AnyAsync(i => i.CompanyId == companyId && i.InvoiceNumber == invoiceNumber, cancellationToken);

        if (duplicate)
        {
            return (false, $"Invoice {invoiceNumber} already exists.", null);
        }

        var arAccountId = await QuickBooksImportAccountResolver.FindAccountsReceivableAsync(unitOfWork, companyId, cancellationToken);
        var revenueAccountId = await QuickBooksImportAccountResolver.FindSalesRevenueAsync(unitOfWork, companyId, cancellationToken);
        var taxAccountId = await QuickBooksImportAccountResolver.FindSalesTaxPayableAsync(unitOfWork, companyId, cancellationToken);

        if (!arAccountId.HasValue || !revenueAccountId.HasValue || !taxAccountId.HasValue)
        {
            return (false, "Could not resolve sales posting accounts.", null);
        }

        var invoice = new SalesInvoice
        {
            CompanyId = companyId,
            InvoiceNumber = invoiceNumber,
            CustomerId = customerId,
            InvoiceDate = invoiceDate.Date,
            InvoiceType = InvoiceType.SalesInvoice,
            ScenarioId = 1,
            SubTotal = netTotal,
            TaxAmount = 0m,
            NetTotal = netTotal,
            Status = InvoiceStatus.Posted,
            CreatedAt = now,
            CreatedBy = ImportUser
        };

        await unitOfWork.Repository<SalesInvoice>().AddAsync(invoice, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await unitOfWork.Repository<SalesInvoiceLine>().AddAsync(new SalesInvoiceLine
        {
            SalesInvoiceId = invoice.Id,
            ItemId = importItemId,
            ProductDescription = "Imported from QuickBooks",
            Quantity = 1m,
            Price = netTotal,
            TaxRate = 0m,
            TaxAmount = 0m,
            LineTotal = netTotal
        }, cancellationToken);

        var journalEntry = new JournalEntry
        {
            CompanyId = companyId,
            EntryNumber = await GenerateNextJournalEntryNumberAsync(unitOfWork, companyId, cancellationToken),
            EntryDate = invoiceDate.Date,
            Description = $"Sales invoice {invoiceNumber}",
            ReferenceType = ReferenceTypes.SalesInvoice,
            ReferenceId = invoice.Id,
            Status = JournalStatus.Posted,
            CreatedAt = now,
            CreatedBy = ImportUser
        };

        await unitOfWork.Repository<JournalEntry>().AddAsync(journalEntry, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await unitOfWork.Repository<JournalEntryLine>().AddRangeAsync(
        [
            CreateJournalLine(arAccountId.Value, netTotal, 0m, "Accounts Receivable", journalEntry.Id),
            CreateJournalLine(revenueAccountId.Value, 0m, netTotal, "Sales Revenue", journalEntry.Id)
        ], cancellationToken);

        invoice.JournalEntryId = journalEntry.Id;
        unitOfWork.Repository<SalesInvoice>().Update(invoice);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (true, null, invoice.Id);
    }

    public static async Task<(bool Success, string? Message, int? BillId)> CreateApprovedVendorBillAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        int vendorId,
        string billNumber,
        DateTime billDate,
        decimal netAmount,
        int importItemId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (netAmount <= 0m)
        {
            return (false, "Bill amount must be greater than zero.", null);
        }

        var duplicate = await unitOfWork.Repository<VendorBill>()
            .Query()
            .AnyAsync(b => b.CompanyId == companyId && b.BillNumber == billNumber, cancellationToken);

        if (duplicate)
        {
            return (false, $"Bill {billNumber} already exists.", null);
        }

        var payableAccountId = await QuickBooksImportAccountResolver.FindAccountsPayableAsync(unitOfWork, companyId, cancellationToken);
        var expenseAccountId = await QuickBooksImportAccountResolver.FindPurchasesOrInventoryAsync(unitOfWork, companyId, cancellationToken);

        if (!payableAccountId.HasValue || !expenseAccountId.HasValue)
        {
            return (false, "Could not resolve vendor bill posting accounts.", null);
        }

        var bill = new VendorBill
        {
            CompanyId = companyId,
            VendorId = vendorId,
            BillNumber = billNumber,
            BillDate = billDate.Date,
            TotalQuantity = 1m,
            NetAmount = netAmount,
            TaxAmount = 0m,
            Status = BillStatus.Approved,
            CreatedAt = now,
            CreatedBy = ImportUser
        };

        await unitOfWork.Repository<VendorBill>().AddAsync(bill, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await unitOfWork.Repository<VendorBillLine>().AddAsync(new VendorBillLine
        {
            VendorBillId = bill.Id,
            ItemId = importItemId,
            Description = "Imported from QuickBooks",
            Quantity = 1m,
            Rate = netAmount,
            Amount = netAmount
        }, cancellationToken);

        var journalEntry = new JournalEntry
        {
            CompanyId = companyId,
            EntryNumber = await GenerateNextJournalEntryNumberAsync(unitOfWork, companyId, cancellationToken),
            EntryDate = billDate.Date,
            Description = $"Vendor bill {billNumber}",
            ReferenceType = ReferenceTypes.VendorBill,
            ReferenceId = bill.Id,
            Status = JournalStatus.Posted,
            CreatedAt = now,
            CreatedBy = ImportUser
        };

        await unitOfWork.Repository<JournalEntry>().AddAsync(journalEntry, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await unitOfWork.Repository<JournalEntryLine>().AddRangeAsync(
        [
            CreateJournalLine(expenseAccountId.Value, netAmount, 0m, "Purchases", journalEntry.Id),
            CreateJournalLine(payableAccountId.Value, 0m, netAmount, "Accounts Payable", journalEntry.Id)
        ], cancellationToken);

        bill.JournalEntryId = journalEntry.Id;
        unitOfWork.Repository<VendorBill>().Update(bill);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (true, null, bill.Id);
    }

    private static async Task RemoveJournalByReferenceAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        string referenceType,
        int referenceId,
        CancellationToken cancellationToken)
    {
        var journalIds = await unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Where(j => j.CompanyId == companyId
                        && j.ReferenceType == referenceType
                        && j.ReferenceId == referenceId)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        if (journalIds.Count == 0)
        {
            return;
        }

        var lines = await unitOfWork.Repository<JournalEntryLine>()
            .Query(asNoTracking: false)
            .Where(l => journalIds.Contains(l.JournalEntryId))
            .ToListAsync(cancellationToken);

        unitOfWork.Repository<JournalEntryLine>().RemoveRange(lines);

        var entries = await unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Where(j => journalIds.Contains(j.Id))
            .ToListAsync(cancellationToken);

        unitOfWork.Repository<JournalEntry>().RemoveRange(entries);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static async Task<(bool Success, string? Message)> CreatePostedJournalAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        DateTime entryDate,
        string description,
        string referenceType,
        int referenceId,
        IReadOnlyList<JournalEntryLine> lines,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var journalEntry = new JournalEntry
        {
            CompanyId = companyId,
            EntryNumber = await GenerateNextJournalEntryNumberAsync(unitOfWork, companyId, cancellationToken),
            EntryDate = entryDate,
            Description = description,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Status = JournalStatus.Posted,
            CreatedAt = now,
            CreatedBy = ImportUser
        };

        await unitOfWork.Repository<JournalEntry>().AddAsync(journalEntry, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var line in lines)
        {
            line.JournalEntryId = journalEntry.Id;
        }

        await unitOfWork.Repository<JournalEntryLine>().AddRangeAsync(lines, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    private static JournalEntryLine CreateJournalLine(
        int accountId,
        decimal debit,
        decimal credit,
        string memo,
        int? journalEntryId = null) =>
        new()
        {
            JournalEntryId = journalEntryId ?? 0,
            ChartOfAccountId = accountId,
            Debit = Math.Round(debit, 2),
            Credit = Math.Round(credit, 2),
            Memo = memo
        };

    private static async Task<string> GenerateNextJournalEntryNumberAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        CancellationToken cancellationToken)
    {
        var prefix = AppConstants.JournalEntryNumberPrefix;
        var numbers = await unitOfWork.Repository<JournalEntry>()
            .Query()
            .Where(j => j.CompanyId == companyId && j.EntryNumber.StartsWith(prefix))
            .Select(j => j.EntryNumber)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var number in numbers)
        {
            var match = JournalEntryNumberRegex().Match(number);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var value))
            {
                max = Math.Max(max, value);
            }
        }

        return $"{prefix}{(max + 1):D4}";
    }

    [GeneratedRegex(@"^JE-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex JournalEntryNumberRegex();
}
