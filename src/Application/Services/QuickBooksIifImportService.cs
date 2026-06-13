using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Import;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class QuickBooksIifImportService : IQuickBooksIifImportService
{
    private const int BatchSize = 500;
    private const string ImportUser = "quickbooks-iif-import";
    private const string OpeningStockRefNo = "OPENING-31MAY2026";
    private const string OpeningStockBillNumber = "OPEN-STOCK-31052026";
    private static readonly DateTime OpeningStockDate = new(2026, 5, 31);
    private const string OpeningStockVendorCode = "OPENING-STOCK";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IItemCartonSyncService _itemCartonSyncService;
    private readonly ILogger<QuickBooksIifImportService> _logger;

    public QuickBooksIifImportService(
        IUnitOfWork unitOfWork,
        IItemCartonSyncService itemCartonSyncService,
        ILogger<QuickBooksIifImportService> logger)
    {
        _unitOfWork = unitOfWork;
        _itemCartonSyncService = itemCartonSyncService;
        _logger = logger;
    }

    public async Task<QuickBooksIifImportResult> ImportAsync(
        string filePath,
        int companyId,
        QuickBooksIifImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new QuickBooksIifImportOptions();

        if (!File.Exists(filePath))
        {
            return Failed($"IIF file not found: {filePath}");
        }

        var companyExists = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => c.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return Failed($"Company id {companyId} was not found.");
        }

        var document = QuickBooksIifParser.Parse(filePath);
        var now = DateTime.UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var accountResult = (Imported: 0, Skipped: 0);
            var itemResult = (Imported: 0, Skipped: 0);
            var customerResult = (Imported: 0, Skipped: 0);
            var vendorResult = (Imported: 0, Skipped: 0);

            if (!options.SkipMasterData)
            {
                accountResult = await ImportAccountsAsync(document.Accounts, companyId, now, cancellationToken);
                itemResult = await ImportItemsAsync(document.InvItems, companyId, now, cancellationToken);
                customerResult = await ImportCustomersAsync(document.Customers, companyId, now, cancellationToken);
                vendorResult = await ImportVendorsAsync(document.Vendors, companyId, now, cancellationToken);
            }

            var transactionResult = await ImportTransactionsAsync(document.Transactions, companyId, now, cancellationToken);
            var reportResult = await ImportReportsInternalAsync(companyId, options, now, cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            if (document.Transactions.Count == 0
                && reportResult.CustomerBalancesUpdated == 0
                && reportResult.VendorBalancesUpdated == 0
                && reportResult.InvoicesImported == 0
                && reportResult.BillsImported == 0
                && string.IsNullOrWhiteSpace(options.CustomerBalancesCsvPath)
                && string.IsNullOrWhiteSpace(options.VendorBalancesCsvPath)
                && string.IsNullOrWhiteSpace(options.OpenInvoicesCsvPath)
                && string.IsNullOrWhiteSpace(options.OpenBillsCsvPath))
            {
                _logger.LogWarning(
                    "IIF file {FilePath} contains master lists only. Customer/vendor balances and invoices/bills require QuickBooks report CSV exports or a transaction IIF file.",
                    filePath);
            }

            var message =
                $"Imported QuickBooks data into company {companyId}: " +
                $"{accountResult.Imported} accounts, {itemResult.Imported} items, " +
                $"{customerResult.Imported} customers, {vendorResult.Imported} vendors, " +
                $"{transactionResult.InvoicesImported + reportResult.InvoicesImported} invoices, " +
                $"{transactionResult.BillsImported + reportResult.BillsImported} bills, " +
                $"{reportResult.CustomerBalancesUpdated} customer balances, " +
                $"{reportResult.VendorBalancesUpdated} vendor balances.";

            _logger.LogInformation(message);

            return new QuickBooksIifImportResult(
                true,
                message,
                accountResult.Imported,
                itemResult.Imported,
                customerResult.Imported,
                vendorResult.Imported,
                accountResult.Skipped,
                itemResult.Skipped,
                customerResult.Skipped,
                vendorResult.Skipped,
                transactionResult.InvoicesImported + reportResult.InvoicesImported,
                transactionResult.BillsImported + reportResult.BillsImported,
                transactionResult.CustomerReceiptsImported,
                transactionResult.VendorPaymentsImported,
                reportResult.CustomerBalancesUpdated,
                reportResult.VendorBalancesUpdated,
                transactionResult.InvoicesSkipped + reportResult.InvoicesSkipped,
                transactionResult.BillsSkipped + reportResult.BillsSkipped,
                reportResult.ItemsStockUpdated,
                reportResult.ItemsStockSkipped);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "QuickBooks IIF import failed for company {CompanyId}", companyId);
            return Failed($"Import failed: {ex.Message}");
        }
    }

    public async Task<QuickBooksIifImportResult> ImportReportsAsync(
        int companyId,
        QuickBooksIifImportOptions options,
        CancellationToken cancellationToken = default)
    {
        var companyExists = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => c.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return Failed($"Company id {companyId} was not found.");
        }

        var now = DateTime.UtcNow;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var reportResult = await ImportReportsInternalAsync(companyId, options, now, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var message =
                $"Updated QuickBooks report data for company {companyId}: " +
                $"{reportResult.ItemsStockUpdated} item stock balances, " +
                $"{reportResult.CustomerBalancesUpdated} customer balances, " +
                $"{reportResult.VendorBalancesUpdated} vendor balances, " +
                $"{reportResult.InvoicesImported} invoices, {reportResult.BillsImported} bills.";

            return new QuickBooksIifImportResult(
                true,
                message,
                CustomerBalancesUpdated: reportResult.CustomerBalancesUpdated,
                VendorBalancesUpdated: reportResult.VendorBalancesUpdated,
                InvoicesImported: reportResult.InvoicesImported,
                BillsImported: reportResult.BillsImported,
                InvoicesSkipped: reportResult.InvoicesSkipped,
                BillsSkipped: reportResult.BillsSkipped,
                ItemsStockUpdated: reportResult.ItemsStockUpdated,
                ItemsStockSkipped: reportResult.ItemsStockSkipped);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "QuickBooks report import failed for company {CompanyId}", companyId);
            return Failed($"Report import failed: {ex.Message}");
        }
    }

    private static QuickBooksIifImportResult Failed(string message) =>
        new(false, message);

    private async Task<(int Imported, int Skipped)> ImportAccountsAsync(
        IReadOnlyList<IifRecord> records,
        int companyId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existingNumbers = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId)
            .Select(a => a.AccountNumber)
            .ToListAsync(cancellationToken);

        var existingSet = existingNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var accountIdByFullName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var ordered = records
            .Where(r => !ShouldSkipAccount(r))
            .OrderBy(r => GetHierarchyDepth(r.Get("NAME")))
            .ThenBy(r => r.Get("NAME"), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var imported = 0;
        var skipped = 0;

        foreach (var record in ordered)
        {
            var fullName = record.Get("NAME").Trim();
            if (string.IsNullOrWhiteSpace(fullName))
            {
                skipped++;
                continue;
            }

            var accountNumber = ResolveAccountNumber(record, fullName);
            if (existingSet.Contains(accountNumber))
            {
                skipped++;
                var existingId = await _unitOfWork.Repository<ChartOfAccount>()
                    .Query()
                    .Where(a => a.CompanyId == companyId && a.AccountNumber == accountNumber)
                    .Select(a => a.Id)
                    .FirstAsync(cancellationToken);
                accountIdByFullName[fullName] = existingId;
                continue;
            }

            var (typeId, subTypeId) = MapAccountType(record, fullName);
            var parentAccountId = ResolveParentAccountId(fullName, accountIdByFullName);
            var leafName = GetLeafName(fullName);
            var hidden = string.Equals(record.Get("HIDDEN"), "Y", StringComparison.OrdinalIgnoreCase);

            var entity = new ChartOfAccount
            {
                CompanyId = companyId,
                AccountNumber = accountNumber,
                AccountName = leafName,
                TypeId = typeId,
                SubTypeId = subTypeId,
                ParentAccountId = parentAccountId,
                Description = NullIfEmpty(record.Get("DESC")),
                OpeningBalance = QuickBooksIifParser.ParseAmount(record.Get("OBAMOUNT")),
                IsActive = !hidden,
                CreatedAt = now,
                CreatedBy = ImportUser
            };

            await _unitOfWork.Repository<ChartOfAccount>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            accountIdByFullName[fullName] = entity.Id;
            existingSet.Add(accountNumber);
            imported++;
        }

        return (imported, skipped);
    }

    private async Task<(int Imported, int Skipped)> ImportItemsAsync(
        IReadOnlyList<IifRecord> records,
        int companyId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existingCodes = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId)
            .Select(i => i.ItemCode)
            .ToListAsync(cancellationToken);

        var existingSet = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        var skipped = 0;
        var batch = new List<Item>();

        foreach (var record in records)
        {
            var itemType = record.Get("INVITEMTYPE").Trim().ToUpperInvariant();
            if (itemType is not ("INVENTORY" or "SERV"))
            {
                skipped++;
                continue;
            }

            var itemCode = record.Get("NAME").Trim();
            if (string.IsNullOrWhiteSpace(itemCode) || existingSet.Contains(itemCode))
            {
                skipped++;
                continue;
            }

            var salesAccount = record.Get("ACCNT");
            var unitOfMeasureId = ResolveUnitOfMeasureId(itemType, salesAccount);
            var (stackNo, lotNo) = ExtractStackLot(record.Get("DESC"), itemCode);

            batch.Add(new Item
            {
                CompanyId = companyId,
                ItemType = itemType == "SERV" ? ItemType.Service : ItemType.Goods,
                ItemCode = itemCode,
                ItemName = itemCode,
                StackNo = stackNo,
                LotNo = lotNo,
                Description = NullIfEmpty(record.Get("DESC")) ?? NullIfEmpty(record.Get("PURCHASEDESC")),
                UnitOfMeasureId = unitOfMeasureId,
                PurchaseRate = QuickBooksIifParser.ParseAmount(record.Get("COST")),
                SaleRate = QuickBooksIifParser.ParseAmount(record.Get("PRICE")),
                CurrentStock = QuickBooksIifParser.ParseAmount(record.Get("QNTY")),
                IsActive = !string.Equals(record.Get("HIDDEN"), "Y", StringComparison.OrdinalIgnoreCase),
                CreatedAt = now,
                CreatedBy = ImportUser
            });

            existingSet.Add(itemCode);

            if (batch.Count >= BatchSize)
            {
                await _unitOfWork.Repository<Item>().AddRangeAsync(batch, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                imported += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _unitOfWork.Repository<Item>().AddRangeAsync(batch, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            imported += batch.Count;
        }

        return (imported, skipped);
    }

    private async Task<(int Imported, int Skipped)> ImportCustomersAsync(
        IReadOnlyList<IifRecord> records,
        int companyId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existingNames = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId)
            .Select(c => c.BuyerName)
            .ToListAsync(cancellationToken);

        var existingNameSet = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextBuyerNumber = await GetNextCustomerNumberAsync(companyId, cancellationToken);
        var imported = 0;
        var skipped = 0;
        var batch = new List<Customer>();

        foreach (var record in records)
        {
            if (string.Equals(record.Get("HIDDEN"), "Y", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            var buyerName = record.Get("NAME").Trim();
            if (string.IsNullOrWhiteSpace(buyerName) || existingNameSet.Contains(buyerName))
            {
                skipped++;
                continue;
            }

            var address = BuildAddress(
                record.Get("BADDR1"),
                record.Get("BADDR2"),
                record.Get("BADDR3"),
                record.Get("BADDR4"),
                record.Get("BADDR5"));

            batch.Add(new Customer
            {
                CompanyId = companyId,
                BuyerId = $"{AppConstants.CustomerIdPrefix}{nextBuyerNumber:D4}",
                BuyerName = buyerName,
                Address = address,
                Phone = NullIfEmpty(record.Get("PHONE1")) ?? NullIfEmpty(record.Get("PHONE2")),
                Email = NullIfEmpty(record.Get("EMAIL")),
                CustomerType = ResolveCustomerType(record),
                ScenarioId = 1,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = ImportUser
            });

            existingNameSet.Add(buyerName);
            nextBuyerNumber++;

            if (batch.Count >= BatchSize)
            {
                await _unitOfWork.Repository<Customer>().AddRangeAsync(batch, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                imported += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _unitOfWork.Repository<Customer>().AddRangeAsync(batch, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            imported += batch.Count;
        }

        return (imported, skipped);
    }

    private async Task<(int Imported, int Skipped)> ImportVendorsAsync(
        IReadOnlyList<IifRecord> records,
        int companyId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existingNames = await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId)
            .Select(v => v.VendorName)
            .ToListAsync(cancellationToken);

        var existingNameSet = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextVendorNumber = await GetNextVendorNumberAsync(companyId, cancellationToken);
        var imported = 0;
        var skipped = 0;
        var batch = new List<Vendor>();

        foreach (var record in records)
        {
            if (string.Equals(record.Get("HIDDEN"), "Y", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            var vendorName = record.Get("NAME").Trim();
            if (string.IsNullOrWhiteSpace(vendorName) || existingNameSet.Contains(vendorName))
            {
                skipped++;
                continue;
            }

            var address = BuildAddress(
                record.Get("ADDR1"),
                record.Get("ADDR2"),
                record.Get("ADDR3"),
                record.Get("ADDR4"),
                record.Get("ADDR5"));

            batch.Add(new Vendor
            {
                CompanyId = companyId,
                VendorCode = $"{AppConstants.VendorCodePrefix}{nextVendorNumber:D4}",
                VendorName = vendorName,
                Address = address,
                Phone = NullIfEmpty(record.Get("PHONE1")) ?? NullIfEmpty(record.Get("PHONE2")),
                Email = NullIfEmpty(record.Get("EMAIL")),
                NTN = NullIfEmpty(record.Get("TAXID")),
                IsActive = true,
                CreatedAt = now,
                CreatedBy = ImportUser
            });

            existingNameSet.Add(vendorName);
            nextVendorNumber++;

            if (batch.Count >= BatchSize)
            {
                await _unitOfWork.Repository<Vendor>().AddRangeAsync(batch, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                imported += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _unitOfWork.Repository<Vendor>().AddRangeAsync(batch, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            imported += batch.Count;
        }

        return (imported, skipped);
    }

    private static bool ShouldSkipAccount(IifRecord record)
    {
        var accountType = record.Get("ACCNTTYPE").Trim().ToUpperInvariant();
        return accountType is "NONPOSTING";
    }

    private static int GetHierarchyDepth(string fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? 0 : fullName.Split(':').Length;

    private static string GetLeafName(string fullName)
    {
        var parts = fullName.Split(':');
        return parts[^1].Trim();
    }

    private static string ResolveAccountNumber(IifRecord record, string fullName)
    {
        var accountNumber = record.Get("ACCNUM").Trim();
        if (!string.IsNullOrWhiteSpace(accountNumber))
        {
            return accountNumber;
        }

        return $"QB-{fullName.Replace(':', '-').Replace(' ', '-')}";
    }

    private static int? ResolveParentAccountId(
        string fullName,
        IReadOnlyDictionary<string, int> accountIdByFullName)
    {
        var separatorIndex = fullName.LastIndexOf(':');
        if (separatorIndex <= 0)
        {
            return null;
        }

        var parentName = fullName[..separatorIndex];
        return accountIdByFullName.TryGetValue(parentName, out var parentId) ? parentId : null;
    }

    private static (int TypeId, int SubTypeId) MapAccountType(IifRecord record, string fullName)
    {
        var accountType = record.Get("ACCNTTYPE").Trim().ToUpperInvariant();
        var extra = record.Get("EXTRA").Trim().ToUpperInvariant();
        var normalizedName = fullName.ToUpperInvariant();

        return accountType switch
        {
            "BANK" => (1, 1),
            "AR" => (1, 2),
            "OCASSET" when normalizedName.Contains("INVENTORY", StringComparison.Ordinal) => (1, 3),
            "OCASSET" when normalizedName.Contains("TAX", StringComparison.Ordinal) => (1, 6),
            "OCASSET" => (1, 7),
            "FIXASSET" => (1, 5),
            "AP" => (2, 8),
            "OCLIAB" when normalizedName.Contains("TAX", StringComparison.Ordinal) => (2, 10),
            "OCLIAB" => (2, 13),
            "EQUITY" when extra.Contains("OPENBAL", StringComparison.Ordinal) => (3, 14),
            "EQUITY" when normalizedName.Contains("DRAW", StringComparison.Ordinal) => (3, 16),
            "EQUITY" => (3, 15),
            "INC" => (4, 18),
            "COGS" => (5, 22),
            "EXEXP" => (6, 35),
            "EXP" when normalizedName.Contains("PAYROLL", StringComparison.Ordinal) => (6, 30),
            "EXP" when normalizedName.Contains("RENT", StringComparison.Ordinal) => (6, 31),
            "EXP" when normalizedName.Contains("UTIL", StringComparison.Ordinal) => (6, 31),
            "EXP" when normalizedName.Contains("DEPRE", StringComparison.Ordinal) => (6, 32),
            "EXP" when normalizedName.Contains("INTEREST", StringComparison.Ordinal) => (6, 33),
            _ => (6, 28)
        };
    }

    private static int ResolveUnitOfMeasureId(string itemType, string salesAccount)
    {
        if (itemType == "SERV")
        {
            return 3;
        }

        return salesAccount.Contains("(Carton)", StringComparison.OrdinalIgnoreCase) ? 4 : 1;
    }

    private static int ResolveUnitOfMeasureFromValuationRow(string? unitOfMeasure, string itemCode)
    {
        var normalized = unitOfMeasure?.Trim().ToLowerInvariant();
        if (normalized is "ctn" or "carton")
        {
            return 4;
        }

        if (normalized is "kg" or "kilogram" or "kgs")
        {
            return 1;
        }

        if (itemCode.StartsWith('C'))
        {
            return 4;
        }

        if (itemCode.StartsWith('W'))
        {
            return 1;
        }

        return 1;
    }

    private static (string StackNo, string LotNo) ExtractStackLot(string description, string fallbackCode)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return (fallbackCode, fallbackCode);
        }

        string? stackNo = null;
        string? lotNo = null;

        var stackIndex = description.IndexOf("Stack:", StringComparison.OrdinalIgnoreCase);
        if (stackIndex >= 0)
        {
            stackNo = description[(stackIndex + 6)..].Trim();
        }

        var lotIndex = description.IndexOf("Lot:", StringComparison.OrdinalIgnoreCase);
        if (lotIndex >= 0)
        {
            var lotPart = description[(lotIndex + 4)..];
            var stackInLot = lotPart.IndexOf("Stack:", StringComparison.OrdinalIgnoreCase);
            lotNo = (stackInLot >= 0 ? lotPart[..stackInLot] : lotPart).Trim();
        }

        return (stackNo ?? fallbackCode, lotNo ?? fallbackCode);
    }

    private static CustomerType ResolveCustomerType(IifRecord record)
    {
        var customFields = string.Join(' ',
        [
            record.Get("CUSTFLD1"),
            record.Get("CUSTFLD2"),
            record.Get("CUSTFLD3"),
            record.Get("CUSTFLD4"),
            record.Get("CUSTFLD5")
        ]);

        return customFields.Contains("Unregistered", StringComparison.OrdinalIgnoreCase)
            || customFields.Contains("Un-Registered", StringComparison.OrdinalIgnoreCase)
            ? CustomerType.Unregistered
            : CustomerType.Registered;
    }

    private static string? BuildAddress(params string?[] parts)
    {
        var values = parts
            .Select(NullIfEmpty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return values.Count == 0 ? null : string.Join(", ", values);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Item? FindItemForValuationRow(IReadOnlyList<Item> items, string itemKey)
    {
        var codeMatches = items
            .Where(i => string.Equals(i.ItemCode, itemKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (codeMatches.Count == 1)
        {
            return codeMatches[0];
        }

        if (codeMatches.Count > 1)
        {
            return codeMatches[0];
        }

        var nameMatches = items
            .Where(i => string.Equals(i.ItemName, itemKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return nameMatches.Count switch
        {
            1 => nameMatches[0],
            > 1 => nameMatches[0],
            _ => null
        };
    }

    private sealed record ReportImportResult(
        int CustomerBalancesUpdated,
        int VendorBalancesUpdated,
        int InvoicesImported,
        int BillsImported,
        int InvoicesSkipped,
        int BillsSkipped,
        int ItemsStockUpdated = 0,
        int ItemsStockSkipped = 0);

    private sealed record TransactionImportResult(
        int InvoicesImported,
        int BillsImported,
        int CustomerReceiptsImported,
        int VendorPaymentsImported,
        int InvoicesSkipped,
        int BillsSkipped);

    private async Task<TransactionImportResult> ImportTransactionsAsync(
        IReadOnlyList<IifTransactionBlock> transactions,
        int companyId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (transactions.Count == 0)
        {
            return new TransactionImportResult(0, 0, 0, 0, 0, 0);
        }

        var customerMap = await BuildCustomerNameMapAsync(companyId, cancellationToken);
        var vendorMap = await BuildVendorNameMapAsync(companyId, cancellationToken);
        var importItemId = await QuickBooksIifImportSupport.GetOrCreateImportItemIdAsync(
            _unitOfWork, companyId, now, cancellationToken);

        var invoicesImported = 0;
        var billsImported = 0;
        var receiptsImported = 0;
        var paymentsImported = 0;
        var invoicesSkipped = 0;
        var billsSkipped = 0;

        foreach (var block in transactions)
        {
            var txnType = block.Trns.Get("TRNSTYPE").Trim().ToUpperInvariant();
            var name = block.Trns.Get("NAME").Trim();
            var amount = Math.Abs(QuickBooksIifParser.ParseAmount(block.Trns.Get("AMOUNT")));
            var docNumber = block.Trns.Get("DOCNUM").Trim();
            var txnDate = QuickBooksIifParser.ParseDate(block.Trns.Get("DATE")) ?? now.Date;

            if (amount == 0m)
            {
                continue;
            }

            switch (txnType)
            {
                case "INVOICE":
                case "SALES RECEIPT" when !string.IsNullOrWhiteSpace(name):
                    if (!customerMap.TryGetValue(name, out var customerId))
                    {
                        invoicesSkipped++;
                        continue;
                    }

                    var invoiceNumber = string.IsNullOrWhiteSpace(docNumber) ? $"QB-{txnType}-{invoicesImported + 1:D5}" : $"QB-{docNumber}";
                    var invoiceResult = await QuickBooksIifImportSupport.CreatePostedSalesInvoiceAsync(
                        _unitOfWork,
                        companyId,
                        customerId,
                        invoiceNumber,
                        txnDate,
                        amount,
                        importItemId,
                        now,
                        cancellationToken);

                    if (invoiceResult.Success)
                    {
                        invoicesImported++;
                    }
                    else
                    {
                        invoicesSkipped++;
                    }

                    break;

                case "BILL":
                    if (!vendorMap.TryGetValue(name, out var vendorId))
                    {
                        billsSkipped++;
                        continue;
                    }

                    var billNumber = string.IsNullOrWhiteSpace(docNumber) ? $"QB-BILL-{billsImported + 1:D5}" : $"QB-{docNumber}";
                    var billResult = await QuickBooksIifImportSupport.CreateApprovedVendorBillAsync(
                        _unitOfWork,
                        companyId,
                        vendorId,
                        billNumber,
                        txnDate,
                        amount,
                        importItemId,
                        now,
                        cancellationToken);

                    if (billResult.Success)
                    {
                        billsImported++;
                    }
                    else
                    {
                        billsSkipped++;
                    }

                    break;

                case "PAYMENT":
                case "CUSTPMT":
                case "RECEIVE PAYMENT":
                    if (!customerMap.TryGetValue(name, out var receiptCustomerId))
                    {
                        continue;
                    }

                    await _unitOfWork.Repository<CustomerReceipt>().AddAsync(new CustomerReceipt
                    {
                        CompanyId = companyId,
                        ReceiptNumber = string.IsNullOrWhiteSpace(docNumber) ? $"QB-RCP-{receiptsImported + 1:D5}" : $"QB-{docNumber}",
                        CustomerId = receiptCustomerId,
                        ReceiptDate = txnDate,
                        Amount = amount,
                        PaymentMethod = PaymentMethod.Cash,
                        CreatedAt = now,
                        CreatedBy = ImportUser
                    }, cancellationToken);
                    receiptsImported++;
                    break;

                case "BILL PMT":
                case "BILLPMT":
                case "CHECK" when vendorMap.ContainsKey(name):
                    if (!vendorMap.TryGetValue(name, out var paymentVendorId))
                    {
                        continue;
                    }

                    await _unitOfWork.Repository<VendorPayment>().AddAsync(new VendorPayment
                    {
                        CompanyId = companyId,
                        PaymentNumber = string.IsNullOrWhiteSpace(docNumber) ? $"QB-VPAY-{paymentsImported + 1:D5}" : $"QB-{docNumber}",
                        VendorId = paymentVendorId,
                        PaymentDate = txnDate,
                        Amount = amount,
                        PaymentMethod = PaymentMethod.Cash,
                        CreatedAt = now,
                        CreatedBy = ImportUser
                    }, cancellationToken);
                    paymentsImported++;
                    break;
            }
        }

        if (receiptsImported > 0 || paymentsImported > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new TransactionImportResult(
            invoicesImported,
            billsImported,
            receiptsImported,
            paymentsImported,
            invoicesSkipped,
            billsSkipped);
    }

    private async Task<ReportImportResult> ImportReportsInternalAsync(
        int companyId,
        QuickBooksIifImportOptions options,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var customerBalancesUpdated = 0;
        var vendorBalancesUpdated = 0;
        var invoicesImported = 0;
        var billsImported = 0;
        var invoicesSkipped = 0;
        var billsSkipped = 0;
        var itemsStockUpdated = 0;
        var itemsStockSkipped = 0;

        if (!string.IsNullOrWhiteSpace(options.InventoryValuationCsvPath))
        {
            (itemsStockUpdated, itemsStockSkipped) = await ImportInventoryValuationCsvAsync(
                options.InventoryValuationCsvPath,
                companyId,
                options,
                now,
                cancellationToken);
        }

        var openingEntryDate = (options.CutoverDate ?? now).Date;

        if (!string.IsNullOrWhiteSpace(options.CustomerBalancesCsvPath))
        {
            customerBalancesUpdated = await ImportCustomerBalancesCsvAsync(
                options.CustomerBalancesCsvPath,
                companyId,
                openingEntryDate,
                now,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(options.VendorBalancesCsvPath))
        {
            vendorBalancesUpdated = await ImportVendorBalancesCsvAsync(
                options.VendorBalancesCsvPath,
                companyId,
                openingEntryDate,
                now,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(options.OpenInvoicesCsvPath))
        {
            (invoicesImported, invoicesSkipped) = await ImportOpenInvoicesCsvAsync(
                options.OpenInvoicesCsvPath,
                companyId,
                now,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(options.OpenBillsCsvPath))
        {
            (billsImported, billsSkipped) = await ImportOpenBillsCsvAsync(
                options.OpenBillsCsvPath,
                companyId,
                now,
                cancellationToken);
        }

        return new ReportImportResult(
            customerBalancesUpdated,
            vendorBalancesUpdated,
            invoicesImported,
            billsImported,
            invoicesSkipped,
            billsSkipped,
            itemsStockUpdated,
            itemsStockSkipped);
    }

    private async Task<(int Updated, int Skipped)> ImportInventoryValuationCsvAsync(
        string filePath,
        int companyId,
        QuickBooksIifImportOptions options,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Inventory valuation file not found: {filePath}");
        }

        if (QuickBooksReportCsvParser.IsOpeningStockStackLotFormat(filePath))
        {
            return await ImportOpeningStockStackLotCsvAsync(
                filePath,
                companyId,
                options,
                now,
                cancellationToken);
        }

        var rows = QuickBooksReportCsvParser.ParseInventoryValuationReport(filePath);
        var items = await _unitOfWork.Repository<Item>()
            .Query(asNoTracking: false)
            .Where(i => i.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        var updated = 0;
        var skipped = 0;

        foreach (var row in rows)
        {
            var itemKey = QuickBooksReportCsvParser.NormalizeInventoryItemKey(row.ItemName);
            var item = FindItemForValuationRow(items, itemKey);
            if (item is null)
            {
                var rawLabel = row.RawItemLabel ?? row.ItemName;
                var (stackNo, lotNo) = ExtractStackLot(rawLabel, itemKey);
                var unitOfMeasureId = ResolveUnitOfMeasureFromValuationRow(row.UnitOfMeasure, itemKey);

                item = new Item
                {
                    CompanyId = companyId,
                    ItemType = ItemType.Goods,
                    ItemCode = itemKey,
                    ItemName = itemKey,
                    StackNo = stackNo,
                    LotNo = lotNo,
                    Description = row.Description,
                    UnitOfMeasureId = unitOfMeasureId,
                    PurchaseRate = row.AverageCost is > 0m ? Math.Round(row.AverageCost.Value, 2) : 0m,
                    CurrentStock = Math.Round(row.QuantityOnHand, 2),
                    IsActive = true,
                    CreatedAt = now,
                    CreatedBy = ImportUser
                };

                await _unitOfWork.Repository<Item>().AddAsync(item, cancellationToken);
                items.Add(item);
                updated++;
                continue;
            }

            item.CurrentStock = Math.Round(row.QuantityOnHand, 2);
            if (row.AverageCost is > 0m)
            {
                item.PurchaseRate = Math.Round(row.AverageCost.Value, 2);
            }

            var resolvedUnitId = ResolveUnitOfMeasureFromValuationRow(row.UnitOfMeasure, itemKey);
            if (item.UnitOfMeasureId != resolvedUnitId)
            {
                item.UnitOfMeasureId = resolvedUnitId;
            }

            if (!string.IsNullOrWhiteSpace(row.Description) && string.IsNullOrWhiteSpace(item.Description))
            {
                item.Description = row.Description;
            }

            item.UpdatedAt = now;
            item.UpdatedBy = ImportUser;
            _unitOfWork.Repository<Item>().Update(item);
            updated++;
        }

        if (updated > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return (updated, skipped);
    }

    private async Task<(int Updated, int Skipped)> ImportOpeningStockStackLotCsvAsync(
        string filePath,
        int companyId,
        QuickBooksIifImportOptions options,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var quantityOnly = options.OpeningStockQuantityOnly;
        var rows = QuickBooksReportCsvParser.ParseOpeningStockStackLotReport(filePath);
        if (rows.Count == 0)
        {
            return (0, 0);
        }

        await RollbackPreviousOpeningStockAsync(companyId, now, cancellationToken);

        var vendor = await GetOrCreateOpeningStockVendorAsync(companyId, now, cancellationToken);
        var items = await _unitOfWork.Repository<Item>()
            .Query(asNoTracking: false)
            .Where(i => i.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        var pendingLines = new List<(OpeningStockStackLotRow Row, Item Item, decimal Rate, decimal Amount)>();
        var stockByItemCode = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var cartonsByItemCodeAndLot = new Dictionary<(string ItemCode, string LotNo), decimal>();

        foreach (var row in rows)
        {
            var itemCode = row.ItemCode.Trim();
            var item = FindItemForValuationRow(items, itemCode);
            if (item is null)
            {
                var unitId = row.UnitOfMeasureId is > 0
                    ? row.UnitOfMeasureId.Value
                    : ResolveUnitOfMeasureFromValuationRow(null, itemCode);

                item = new Item
                {
                    CompanyId = companyId,
                    ItemType = ItemType.Goods,
                    ItemCode = itemCode,
                    ItemName = string.IsNullOrWhiteSpace(row.ItemName) ? itemCode : row.ItemName.Trim(),
                    StackNo = row.StackNo?.Trim() ?? string.Empty,
                    LotNo = row.LotNo?.Trim() ?? string.Empty,
                    Description = row.Description,
                    HSCode = row.HsCode,
                    Barcode = NormalizeBarcode(row.Barcode),
                    UnitOfMeasureId = unitId,
                    IsActive = true,
                    CreatedAt = now,
                    CreatedBy = ImportUser
                };

                await _unitOfWork.Repository<Item>().AddAsync(item, cancellationToken);
                items.Add(item);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(row.ItemName))
                {
                    item.ItemName = row.ItemName.Trim();
                }

                if (!string.IsNullOrWhiteSpace(row.Description))
                {
                    item.Description = row.Description.Trim();
                }

                if (!string.IsNullOrWhiteSpace(row.HsCode))
                {
                    item.HSCode = row.HsCode.Trim();
                }

                if (!string.IsNullOrWhiteSpace(row.LotNo))
                {
                    item.LotNo = row.LotNo.Trim();
                }

                if (!string.IsNullOrWhiteSpace(row.StackNo) && string.IsNullOrWhiteSpace(item.StackNo))
                {
                    item.StackNo = row.StackNo.Trim();
                }

                if (row.UnitOfMeasureId is > 0)
                {
                    item.UnitOfMeasureId = row.UnitOfMeasureId.Value;
                }
                else
                {
                    item.UnitOfMeasureId = ResolveUnitOfMeasureFromValuationRow(null, itemCode);
                }

                var barcode = NormalizeBarcode(row.Barcode);
                if (!string.IsNullOrWhiteSpace(barcode))
                {
                    item.Barcode = barcode;
                }

                item.UpdatedAt = now;
                item.UpdatedBy = ImportUser;
                _unitOfWork.Repository<Item>().Update(item);
            }

            var quantity = Math.Round(row.Weight, 2);
            var rate = quantityOnly ? 0m : (item.PurchaseRate > 0m ? item.PurchaseRate : 1m);
            var amount = quantityOnly ? 0m : Math.Round(quantity * rate, 2);
            pendingLines.Add((row, item, rate, amount));
            stockByItemCode[itemCode] = stockByItemCode.GetValueOrDefault(itemCode) + quantity;

            var lotNo = row.LotNo?.Trim() ?? string.Empty;
            var cartonKey = (itemCode, lotNo);
            cartonsByItemCodeAndLot[cartonKey] =
                cartonsByItemCodeAndLot.GetValueOrDefault(cartonKey) + Math.Round(row.Cartons, 2);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var bill = new VendorBill
        {
            CompanyId = companyId,
            VendorId = vendor.Id,
            BillNumber = OpeningStockBillNumber,
            RefNo = OpeningStockRefNo,
            BillDate = OpeningStockDate,
            TotalQuantity = pendingLines.Sum(x => Math.Round(x.Row.Weight, 2)),
            TotalCartons = pendingLines.Sum(x => Math.Round(x.Row.Cartons, 2)),
            TaxAmount = 0m,
            NetAmount = quantityOnly ? 0m : pendingLines.Sum(x => x.Amount),
            Status = quantityOnly ? BillStatus.Draft : BillStatus.Approved,
            CreatedAt = now,
            CreatedBy = ImportUser
        };

        await _unitOfWork.Repository<VendorBill>().AddAsync(bill, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var billLines = pendingLines.Select(x => new VendorBillLine
        {
            VendorBillId = bill.Id,
            ItemId = x.Item.Id,
            Description = x.Row.Description,
            StackNo = x.Row.StackNo?.Trim(),
            LotNo = x.Row.LotNo?.Trim(),
            Quantity = Math.Round(x.Row.Weight, 2),
            Cartons = Math.Round(x.Row.Cartons, 2),
            Rate = x.Rate,
            Amount = x.Amount
        }).ToList();

        await _unitOfWork.Repository<VendorBillLine>().AddRangeAsync(billLines, cancellationToken);

        var warehouseId = await GetDefaultWarehouseIdAsync(companyId, cancellationToken);
        if (warehouseId.HasValue)
        {
            var inventoryTransactions = pendingLines
                .Where(x => Math.Round(x.Row.Weight, 2) > 0m)
                .Select(x => new InventoryTransaction
                {
                    CompanyId = companyId,
                    ItemId = x.Item.Id,
                    WarehouseId = warehouseId.Value,
                    TransactionType = InventoryTransactionType.Opening,
                    StackNo = string.IsNullOrWhiteSpace(x.Row.StackNo) ? null : x.Row.StackNo.Trim(),
                    LotNo = string.IsNullOrWhiteSpace(x.Row.LotNo) ? null : x.Row.LotNo.Trim(),
                    Quantity = Math.Round(x.Row.Weight, 2),
                    UnitCost = x.Rate,
                    TotalCost = x.Amount,
                    TransactionDate = OpeningStockDate,
                    ReferenceNo = OpeningStockBillNumber,
                    Notes = $"Opening stock {OpeningStockBillNumber}",
                    CreatedAt = now,
                    CreatedBy = ImportUser
                })
                .ToList();

            if (inventoryTransactions.Count > 0)
            {
                await _unitOfWork.Repository<InventoryTransaction>().AddRangeAsync(
                    inventoryTransactions,
                    cancellationToken);
            }
        }

        if (quantityOnly)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var updatedItemIds = new HashSet<int>();
        foreach (var item in items.Where(i =>
                     stockByItemCode.ContainsKey(i.ItemCode)
                     || cartonsByItemCodeAndLot.Keys.Any(k =>
                         string.Equals(k.ItemCode, i.ItemCode, StringComparison.OrdinalIgnoreCase))))
        {
            if (quantityOnly)
            {
                item.CurrentStock = await RecalculateCurrentStockFromTransactionsAsync(
                    companyId,
                    item.Id,
                    cancellationToken);
            }
            else if (stockByItemCode.TryGetValue(item.ItemCode, out var stock))
            {
                item.CurrentStock = Math.Round(stock, 2);
            }

            var itemLot = item.LotNo?.Trim() ?? string.Empty;
            var cartonKey = (item.ItemCode, itemLot);
            if (cartonsByItemCodeAndLot.TryGetValue(cartonKey, out var cartons))
            {
                item.Cartons = Math.Round(cartons, 2);
            }

            item.UpdatedAt = now;
            item.UpdatedBy = ImportUser;
            _unitOfWork.Repository<Item>().Update(item);
            updatedItemIds.Add(item.Id);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (!quantityOnly)
        {
            await _itemCartonSyncService.SyncItemsAsync(companyId, updatedItemIds, cancellationToken);
        }

        _logger.LogInformation(
            "Imported opening stock for company {CompanyId}: {LineCount} stack/lot lines, {ItemCount} items updated (quantityOnly={QuantityOnly}).",
            companyId,
            billLines.Count,
            updatedItemIds.Count,
            quantityOnly);

        return (billLines.Count, 0);
    }

    private async Task RollbackPreviousOpeningStockAsync(
        int companyId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var openingBillIds = await _unitOfWork.Repository<VendorBill>()
            .Query(asNoTracking: false)
            .Where(b => b.CompanyId == companyId
                        && (b.RefNo == OpeningStockRefNo || b.BillNumber == OpeningStockBillNumber))
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        var affectedItemIds = new HashSet<int>();

        if (openingBillIds.Count > 0)
        {
            var billLines = await _unitOfWork.Repository<VendorBillLine>()
                .Query(asNoTracking: false)
                .Where(l => openingBillIds.Contains(l.VendorBillId))
                .ToListAsync(cancellationToken);

            foreach (var line in billLines)
            {
                if (line.ItemId.HasValue)
                {
                    affectedItemIds.Add(line.ItemId.Value);
                }

                _unitOfWork.Repository<VendorBillLine>().Remove(line);
            }

            var bills = await _unitOfWork.Repository<VendorBill>()
                .Query(asNoTracking: false)
                .Where(b => openingBillIds.Contains(b.Id))
                .ToListAsync(cancellationToken);

            foreach (var bill in bills)
            {
                _unitOfWork.Repository<VendorBill>().Remove(bill);
            }
        }

        var openingTransactions = await _unitOfWork.Repository<InventoryTransaction>()
            .Query(asNoTracking: false)
            .Where(t => t.CompanyId == companyId
                        && (t.ReferenceNo == OpeningStockBillNumber
                            || (t.Notes != null && t.Notes.Contains(OpeningStockBillNumber))))
            .ToListAsync(cancellationToken);

        foreach (var transaction in openingTransactions)
        {
            affectedItemIds.Add(transaction.ItemId);
            _unitOfWork.Repository<InventoryTransaction>().Remove(transaction);
        }

        if (openingBillIds.Count > 0 || openingTransactions.Count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (affectedItemIds.Count > 0)
            {
                await RecalculateItemsCurrentStockFromTransactionsAsync(
                    companyId,
                    affectedItemIds,
                    now,
                    cancellationToken);
                await _itemCartonSyncService.SyncItemsAsync(companyId, affectedItemIds, cancellationToken);
            }

            _logger.LogInformation(
                "Rolled back previous opening stock for company {CompanyId}: {BillCount} bills, {TxnCount} transactions, {ItemCount} items recalculated.",
                companyId,
                openingBillIds.Count,
                openingTransactions.Count,
                affectedItemIds.Count);
        }
    }

    public async Task<OpeningStockRepairResult> ReapplyOpeningStockQuantityOnlyAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var companyExists = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => c.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return new OpeningStockRepairResult
            {
                Success = false,
                Message = $"Company id {companyId} was not found."
            };
        }

        var now = DateTime.UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var bill = await _unitOfWork.Repository<VendorBill>()
                .Query(asNoTracking: false)
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(
                    b => b.CompanyId == companyId
                         && (b.RefNo == OpeningStockRefNo || b.BillNumber == OpeningStockBillNumber),
                    cancellationToken);

            if (bill is null)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return new OpeningStockRepairResult
                {
                    Success = false,
                    Message = $"Opening stock bill ({OpeningStockBillNumber}) not found for company {companyId}."
                };
            }

            var billLinesUpdated = 0;
            var affectedItemIds = new HashSet<int>();

            foreach (var line in bill.Lines)
            {
                if (line.Rate != 0m || line.Amount != 0m)
                {
                    line.Rate = 0m;
                    line.Amount = 0m;
                    _unitOfWork.Repository<VendorBillLine>().Update(line);
                    billLinesUpdated++;
                }

                if (line.ItemId.HasValue)
                {
                    affectedItemIds.Add(line.ItemId.Value);
                }
            }

            bill.NetAmount = 0m;
            bill.Status = BillStatus.Draft;
            bill.JournalEntryId = null;
            bill.UpdatedAt = now;
            bill.UpdatedBy = ImportUser;
            _unitOfWork.Repository<VendorBill>().Update(bill);

            var openingTransactions = await _unitOfWork.Repository<InventoryTransaction>()
                .Query(asNoTracking: false)
                .Where(t => t.CompanyId == companyId
                            && (t.ReferenceNo == OpeningStockBillNumber
                                || (t.Notes != null && t.Notes.Contains(OpeningStockBillNumber))))
                .ToListAsync(cancellationToken);

            var transactionsUpdated = 0;
            foreach (var transaction in openingTransactions)
            {
                affectedItemIds.Add(transaction.ItemId);

                if (transaction.UnitCost != 0m || transaction.TotalCost != 0m)
                {
                    transaction.UnitCost = 0m;
                    transaction.TotalCost = 0m;
                    _unitOfWork.Repository<InventoryTransaction>().Update(transaction);
                    transactionsUpdated++;
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var itemsRecalculated = await RecalculateItemsCurrentStockFromTransactionsAsync(
                companyId,
                affectedItemIds,
                now,
                cancellationToken);

            foreach (var itemId in affectedItemIds)
            {
                var item = await _unitOfWork.Repository<Item>()
                    .Query(asNoTracking: false)
                    .FirstOrDefaultAsync(i => i.Id == itemId && i.CompanyId == companyId, cancellationToken);

                if (item is null)
                {
                    continue;
                }

                var itemLot = item.LotNo?.Trim() ?? string.Empty;
                var cartons = bill.Lines
                    .Where(l => l.ItemId == itemId
                                && string.Equals(l.LotNo?.Trim() ?? string.Empty, itemLot, StringComparison.OrdinalIgnoreCase))
                    .Sum(l => Math.Round(l.Cartons, 2));

                if (item.Cartons != cartons)
                {
                    item.Cartons = cartons;
                    item.UpdatedAt = now;
                    item.UpdatedBy = ImportUser;
                    _unitOfWork.Repository<Item>().Update(item);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation(
                "Reapplied opening stock quantity-only for company {CompanyId}: bill {BillId}, {LineCount} lines, {TxnCount} transactions, {ItemCount} items recalculated.",
                companyId,
                bill.Id,
                billLinesUpdated,
                transactionsUpdated,
                itemsRecalculated);

            return new OpeningStockRepairResult
            {
                Success = true,
                Message =
                    $"Opening stock bill {bill.BillNumber} set to Draft with zero amounts; {itemsRecalculated} items recalculated from inventory transactions.",
                BillLinesUpdated = billLinesUpdated,
                TransactionsUpdated = transactionsUpdated,
                ItemsRecalculated = itemsRecalculated
            };
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to reapply opening stock quantity-only for company {CompanyId}.", companyId);
            return new OpeningStockRepairResult
            {
                Success = false,
                Message = $"Repair failed: {ex.Message}"
            };
        }
    }

    private async Task<int> RecalculateItemsCurrentStockFromTransactionsAsync(
        int companyId,
        IEnumerable<int> itemIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var itemIdList = itemIds.Distinct().ToList();
        if (itemIdList.Count == 0)
        {
            return 0;
        }

        var items = await _unitOfWork.Repository<Item>()
            .Query(asNoTracking: false)
            .Where(i => i.CompanyId == companyId && itemIdList.Contains(i.Id))
            .ToListAsync(cancellationToken);

        var recalculated = 0;
        foreach (var item in items)
        {
            var stock = await RecalculateCurrentStockFromTransactionsAsync(
                companyId,
                item.Id,
                cancellationToken);

            if (item.CurrentStock == stock)
            {
                continue;
            }

            item.CurrentStock = stock;
            item.UpdatedAt = now;
            item.UpdatedBy = ImportUser;
            _unitOfWork.Repository<Item>().Update(item);
            recalculated++;
        }

        if (recalculated > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return recalculated;
    }

    private async Task<decimal> RecalculateCurrentStockFromTransactionsAsync(
        int companyId,
        int itemId,
        CancellationToken cancellationToken)
    {
        var transactions = await _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId && t.ItemId == itemId)
            .Select(t => new { t.TransactionType, t.Quantity })
            .ToListAsync(cancellationToken);

        var stock = transactions.Sum(t => GetStockDelta(t.TransactionType, t.Quantity));
        return Math.Round(stock, 2);
    }

    private static decimal GetStockDelta(InventoryTransactionType type, decimal quantity) =>
        type switch
        {
            InventoryTransactionType.StockIn => quantity,
            InventoryTransactionType.Opening => quantity,
            InventoryTransactionType.StockOut => -quantity,
            InventoryTransactionType.Adjustment => quantity,
            _ => 0m
        };

    private async Task<int?> GetDefaultWarehouseIdAsync(int companyId, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<Warehouse>()
            .Query()
            .Where(w => w.CompanyId == companyId && w.IsActive)
            .OrderBy(w => w.Code)
            .Select(w => (int?)w.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<Vendor> GetOrCreateOpeningStockVendorAsync(
        int companyId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var vendor = await _unitOfWork.Repository<Vendor>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(
                v => v.CompanyId == companyId && v.VendorCode == OpeningStockVendorCode,
                cancellationToken);

        if (vendor is not null)
        {
            return vendor;
        }

        vendor = new Vendor
        {
            CompanyId = companyId,
            VendorCode = OpeningStockVendorCode,
            VendorName = "Opening Stock Import",
            DefaultSalesTaxRate = 0m,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = ImportUser
        };

        await _unitOfWork.Repository<Vendor>().AddAsync(vendor, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return vendor;
    }

    private static string? NormalizeBarcode(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return null;
        }

        var value = barcode.Trim();
        return value.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    private async Task<int> ImportCustomerBalancesCsvAsync(
        string filePath,
        int companyId,
        DateTime entryDate,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Customer balances file not found: {filePath}");
        }

        var rows = QuickBooksReportCsvParser.ParseNameBalanceReport(filePath);
        var customerMap = await BuildCustomerNameMapAsync(companyId, cancellationToken);
        var updated = 0;

        foreach (var row in rows)
        {
            if (!customerMap.TryGetValue(row.Name, out var customerId))
            {
                _logger.LogWarning("Customer balance row skipped; customer not found: {CustomerName}", row.Name);
                continue;
            }

            var customer = await _unitOfWork.Repository<Customer>()
                .Query(asNoTracking: false)
                .FirstAsync(c => c.Id == customerId, cancellationToken);

            customer.OpeningBalance = row.Balance;
            customer.UpdatedAt = now;
            customer.UpdatedBy = ImportUser;
            _unitOfWork.Repository<Customer>().Update(customer);

            var postResult = await QuickBooksIifImportSupport.PostCustomerOpeningBalanceAsync(
                _unitOfWork,
                companyId,
                customerId,
                customer.BuyerName,
                row.Balance,
                entryDate,
                now,
                cancellationToken);

            if (!postResult.Success)
            {
                throw new InvalidOperationException(postResult.Message ?? $"Failed to post opening balance for {row.Name}.");
            }

            updated++;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return updated;
    }

    private async Task<int> ImportVendorBalancesCsvAsync(
        string filePath,
        int companyId,
        DateTime entryDate,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Vendor balances CSV not found: {filePath}");
        }

        var rows = QuickBooksReportCsvParser.ParseNameBalanceReport(filePath);
        var vendorMap = await BuildVendorNameMapAsync(companyId, cancellationToken);
        var updated = 0;

        foreach (var row in rows)
        {
            if (!vendorMap.TryGetValue(row.Name, out var vendorId))
            {
                _logger.LogWarning("Vendor balance row skipped; vendor not found: {VendorName}", row.Name);
                continue;
            }

            var vendor = await _unitOfWork.Repository<Vendor>()
                .Query(asNoTracking: false)
                .FirstAsync(v => v.Id == vendorId, cancellationToken);

            vendor.OpeningBalance = row.Balance;
            vendor.UpdatedAt = now;
            vendor.UpdatedBy = ImportUser;
            _unitOfWork.Repository<Vendor>().Update(vendor);

            var postResult = await QuickBooksIifImportSupport.PostVendorOpeningBalanceAsync(
                _unitOfWork,
                companyId,
                vendorId,
                vendor.VendorName,
                row.Balance,
                entryDate,
                now,
                cancellationToken);

            if (!postResult.Success)
            {
                throw new InvalidOperationException(postResult.Message ?? $"Failed to post opening balance for {row.Name}.");
            }

            updated++;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return updated;
    }

    private async Task<(int Imported, int Skipped)> ImportOpenInvoicesCsvAsync(
        string filePath,
        int companyId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Open invoices CSV not found: {filePath}");
        }

        var rows = QuickBooksReportCsvParser.ParseOpenInvoicesReport(filePath);
        var customerMap = await BuildCustomerNameMapAsync(companyId, cancellationToken);
        var importItemId = await QuickBooksIifImportSupport.GetOrCreateImportItemIdAsync(
            _unitOfWork, companyId, now, cancellationToken);

        var imported = 0;
        var skipped = 0;

        foreach (var row in rows)
        {
            if (!customerMap.TryGetValue(row.CustomerName, out var customerId))
            {
                skipped++;
                continue;
            }

            var invoiceNumber = row.InvoiceNumber.StartsWith("QB-", StringComparison.OrdinalIgnoreCase)
                ? row.InvoiceNumber
                : $"QB-{row.InvoiceNumber}";

            var result = await QuickBooksIifImportSupport.CreatePostedSalesInvoiceAsync(
                _unitOfWork,
                companyId,
                customerId,
                invoiceNumber,
                row.InvoiceDate,
                row.Amount,
                importItemId,
                now,
                cancellationToken);

            if (result.Success)
            {
                imported++;
            }
            else
            {
                skipped++;
            }
        }

        return (imported, skipped);
    }

    private async Task<(int Imported, int Skipped)> ImportOpenBillsCsvAsync(
        string filePath,
        int companyId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Open bills CSV not found: {filePath}");
        }

        var rows = QuickBooksReportCsvParser.ParseOpenBillsReport(filePath);
        var vendorMap = await BuildVendorNameMapAsync(companyId, cancellationToken);
        var importItemId = await QuickBooksIifImportSupport.GetOrCreateImportItemIdAsync(
            _unitOfWork, companyId, now, cancellationToken);

        var imported = 0;
        var skipped = 0;

        foreach (var row in rows)
        {
            if (!vendorMap.TryGetValue(row.VendorName, out var vendorId))
            {
                skipped++;
                continue;
            }

            var billNumber = row.BillNumber.StartsWith("QB-", StringComparison.OrdinalIgnoreCase)
                ? row.BillNumber
                : $"QB-{row.BillNumber}";

            var result = await QuickBooksIifImportSupport.CreateApprovedVendorBillAsync(
                _unitOfWork,
                companyId,
                vendorId,
                billNumber,
                row.BillDate,
                row.Amount,
                importItemId,
                now,
                cancellationToken);

            if (result.Success)
            {
                imported++;
            }
            else
            {
                skipped++;
            }
        }

        return (imported, skipped);
    }

    private async Task<Dictionary<string, int>> BuildCustomerNameMapAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var customers = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId)
            .Select(c => new { c.Id, c.BuyerName })
            .ToListAsync(cancellationToken);

        return customers.ToDictionary(
            c => c.BuyerName,
            c => c.Id,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, int>> BuildVendorNameMapAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var vendors = await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId)
            .Select(v => new { v.Id, v.VendorName })
            .ToListAsync(cancellationToken);

        return vendors.ToDictionary(
            v => v.VendorName,
            v => v.Id,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<int> GetNextCustomerNumberAsync(int companyId, CancellationToken cancellationToken)
    {
        var prefix = AppConstants.CustomerIdPrefix;
        var buyerIds = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.BuyerId.StartsWith(prefix))
            .Select(c => c.BuyerId)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var buyerId in buyerIds)
        {
            if (buyerId.Length > prefix.Length
                && int.TryParse(buyerId[prefix.Length..], out var number))
            {
                max = Math.Max(max, number);
            }
        }

        return max + 1;
    }

    private async Task<int> GetNextVendorNumberAsync(int companyId, CancellationToken cancellationToken)
    {
        var prefix = AppConstants.VendorCodePrefix;
        var vendorCodes = await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId && v.VendorCode.StartsWith(prefix))
            .Select(v => v.VendorCode)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var vendorCode in vendorCodes)
        {
            if (vendorCode.Length > prefix.Length
                && int.TryParse(vendorCode[prefix.Length..], out var number))
            {
                max = Math.Max(max, number);
            }
        }

        return max + 1;
    }
}
