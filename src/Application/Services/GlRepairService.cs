using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.Import;
using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;
using static PakistanAccountingERP.Application.Common.Constants.ReferenceTypes;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class GlRepairService : IGlRepairService
{
    private const string CartageItemCode = "ITEM-0002";
    private const int CogsTypeId = 5;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly ICustomerGlPostingService _customerGlPosting;
    private readonly IVendorGlPostingService _vendorGlPosting;
    private readonly IBankGlPostingService _bankGlPosting;
    private readonly IItemCartonSyncService _itemCartonSyncService;
    private readonly ILogger<GlRepairService> _logger;

    public GlRepairService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        ICustomerGlPostingService customerGlPosting,
        IVendorGlPostingService vendorGlPosting,
        IBankGlPostingService bankGlPosting,
        IItemCartonSyncService itemCartonSyncService,
        ILogger<GlRepairService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _customerGlPosting = customerGlPosting;
        _vendorGlPosting = vendorGlPosting;
        _bankGlPosting = bankGlPosting;
        _itemCartonSyncService = itemCartonSyncService;
        _logger = logger;
    }

    public async Task<GlRepairResult> RepairHistoricalEntriesAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "gl-repair";

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var legacyRemapped = await RemapLegacyCoaJournalLinesAsync(companyId, cancellationToken);
            var parentArConsolidated = await ConsolidateParentArAccountAsync(companyId, cancellationToken);
            var (cartageAdded, cartageAdjusted) = await FixCartageJournalLinesAsync(companyId, cancellationToken);
            var cogsAdded = await BackfillSalesInvoiceCogsLinesAsync(companyId, now, userName, cancellationToken);
            var duplicatesRemoved = await SoftDeleteDuplicateReferenceJournalsAsync(companyId, now, userName, cancellationToken);
            var duplicateReceiptsRemoved = await SoftDeleteDuplicateCustomerReceiptJournalsAsync(companyId, now, userName, cancellationToken);
            var duplicateBankTxRemoved = await SoftDeleteDuplicateBankTransactionJournalsAsync(companyId, now, userName, cancellationToken);
            var orphansRemoved = await SoftDeleteOrphanJournalsAsync(companyId, now, userName, cancellationToken);
            var customerObResynced = await ResyncCustomerOpeningBalanceJournalsAsync(companyId, null, cancellationToken);
            var vendorObResynced = await ResyncVendorOpeningBalanceJournalsAsync(companyId, null, cancellationToken);
            var deletedLinesPurged = await PurgeDeletedJournalLinesAsync(cancellationToken);
            await ResetMisImportedCogsOpeningBalanceAsync(companyId, now, userName, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var arBalance = await GetAccountBalanceAsync(companyId, AccountsReceivable, cancellationToken);

            return new GlRepairResult(
                true,
                "Historical GL entries repaired successfully.",
                legacyRemapped,
                cartageAdded,
                cartageAdjusted,
                cogsAdded,
                duplicatesRemoved,
                orphansRemoved,
                parentArConsolidated,
                customerObResynced,
                duplicateReceiptsRemoved,
                duplicateBankTxRemoved,
                deletedLinesPurged,
                arBalance);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "GL repair failed for company {CompanyId}", companyId);
            return new GlRepairResult(false, ex.Message, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0m);
        }
    }

    public async Task<CutoverReconcileResult> ReconcileToOpeningBalancesAsync(
        int companyId,
        DateTime removeTransactionsOnOrAfter,
        CancellationToken cancellationToken = default)
    {
        var cutoverDate = removeTransactionsOnOrAfter.Date;
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "cutover-reconcile";

        var companyExists = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => c.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return new CutoverReconcileResult(
                false,
                $"Company id {companyId} was not found.",
                0,
                0,
                0,
                0,
                0,
                0m,
                0m,
                0m,
                0m);
        }

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var openingReferenceTypes = new[] { ReferenceTypes.Customer, ReferenceTypes.Vendor };
            var transactionalJournals = await _unitOfWork.Repository<JournalEntry>()
                .Query(asNoTracking: false)
                .Where(j =>
                    j.CompanyId == companyId
                    && !j.IsDeleted
                    && !openingReferenceTypes.Contains(j.ReferenceType))
                .ToListAsync(cancellationToken);

            foreach (var journal in transactionalJournals)
            {
                SoftDeleteJournal(journal, now, userName);
                _unitOfWork.Repository<JournalEntry>().Update(journal);
            }

            var invoicesReverted = 0;
            var postedInvoices = await _unitOfWork.Repository<SalesInvoice>()
                .Query(asNoTracking: false)
                .Where(si =>
                    si.CompanyId == companyId
                    && !si.IsDeleted
                    && si.Status == InvoiceStatus.Posted)
                .ToListAsync(cancellationToken);

            foreach (var invoice in postedInvoices)
            {
                invoice.Status = InvoiceStatus.Draft;
                invoice.JournalEntryId = null;
                invoice.UpdatedAt = now;
                invoice.UpdatedBy = userName;
                _unitOfWork.Repository<SalesInvoice>().Update(invoice);
                invoicesReverted++;
            }

            var receiptsRemoved = 0;
            var receipts = await _unitOfWork.Repository<CustomerReceipt>()
                .Query(asNoTracking: false)
                .Where(r =>
                    r.CompanyId == companyId
                    && !r.IsDeleted
                    && r.ReceiptDate >= cutoverDate)
                .ToListAsync(cancellationToken);

            foreach (var receipt in receipts)
            {
                receipt.IsDeleted = true;
                receipt.DeletedAt = now;
                receipt.DeletedBy = userName;
                receipt.UpdatedAt = now;
                receipt.UpdatedBy = userName;
                _unitOfWork.Repository<CustomerReceipt>().Update(receipt);
                receiptsRemoved++;
            }

            var bankTransactionsRemoved = 0;
            var bankTransactions = await _unitOfWork.Repository<BankTransaction>()
                .Query(asNoTracking: false)
                .Where(bt =>
                    bt.CompanyId == companyId
                    && !bt.IsDeleted
                    && bt.TransactionDate >= cutoverDate)
                .ToListAsync(cancellationToken);

            foreach (var transaction in bankTransactions)
            {
                transaction.IsDeleted = true;
                transaction.DeletedAt = now;
                transaction.DeletedBy = userName;
                transaction.JournalEntryId = null;
                transaction.CustomerBalanceEffect = 0m;
                transaction.UpdatedAt = now;
                transaction.UpdatedBy = userName;
                _unitOfWork.Repository<BankTransaction>().Update(transaction);
                bankTransactionsRemoved++;
            }

            var billsReverted = 0;
            var approvedBills = await _unitOfWork.Repository<VendorBill>()
                .Query(asNoTracking: false)
                .Where(vb =>
                    vb.CompanyId == companyId
                    && !vb.IsDeleted
                    && vb.Status == BillStatus.Approved)
                .ToListAsync(cancellationToken);

            foreach (var bill in approvedBills)
            {
                bill.Status = BillStatus.Draft;
                bill.JournalEntryId = null;
                bill.UpdatedAt = now;
                bill.UpdatedBy = userName;
                _unitOfWork.Repository<VendorBill>().Update(bill);
                billsReverted++;
            }

            var controlAccounts = await _unitOfWork.Repository<ChartOfAccount>()
                .Query(asNoTracking: false)
                .Where(a =>
                    a.CompanyId == companyId
                    && (a.AccountNumber == AccountsReceivable || a.AccountNumber == AccountsPayable))
                .ToListAsync(cancellationToken);

            foreach (var account in controlAccounts)
            {
                if (account.OpeningBalance == 0m)
                {
                    continue;
                }

                account.OpeningBalance = 0m;
                account.UpdatedAt = now;
                account.UpdatedBy = userName;
                _unitOfWork.Repository<ChartOfAccount>().Update(account);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var sumCustomerOb = await _unitOfWork.Repository<Customer>()
                .Query()
                .Where(c => c.CompanyId == companyId && !c.IsDeleted)
                .Select(c => c.OpeningBalance)
                .SumAsync(cancellationToken);

            var sumVendorOb = await _unitOfWork.Repository<Vendor>()
                .Query()
                .Where(v => v.CompanyId == companyId && !v.IsDeleted)
                .Select(v => v.OpeningBalance)
                .SumAsync(cancellationToken);

            var arBalance = await GetAccountBalanceAsync(companyId, AccountsReceivable, cancellationToken);
            var apBalance = await GetAccountBalanceAsync(companyId, AccountsPayable, cancellationToken);

            return new CutoverReconcileResult(
                true,
                "Cutover reconciled to opening balances. Transactional GL and post-cutover documents were removed.",
                transactionalJournals.Count,
                invoicesReverted,
                receiptsRemoved,
                bankTransactionsRemoved,
                billsReverted,
                arBalance,
                apBalance,
                sumCustomerOb,
                sumVendorOb);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Cutover reconcile failed for company {CompanyId}", companyId);
            return new CutoverReconcileResult(
                false,
                ex.Message,
                0,
                0,
                0,
                0,
                0,
                0m,
                0m,
                0m,
                0m);
        }
    }

    public async Task<PostCutoverTransactionsResult> PostCutoverTransactionsAsync(
        int companyId,
        DateTime fromDate,
        CancellationToken cancellationToken = default)
    {
        var from = fromDate.Date;
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "post-cutover";

        var transactionalTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            ReferenceTypes.SalesInvoice,
            ReferenceTypes.VendorBill,
            ReferenceTypes.CustomerReceipt,
            ReferenceTypes.BankTransaction,
            ReferenceTypes.VendorPayment,
        };

        var companyExists = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => c.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return new PostCutoverTransactionsResult(
                false,
                $"Company id {companyId} was not found.",
                0,
                0,
                0,
                0,
                0,
                0,
                0m,
                0m,
                0m,
                0m);
        }

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var journals = await _unitOfWork.Repository<JournalEntry>()
                .Query(asNoTracking: false)
                .IgnoreQueryFilters()
                .Where(j =>
                    j.CompanyId == companyId
                    && j.IsDeleted
                    && j.EntryDate >= from
                    && j.ReferenceType != null
                    && transactionalTypes.Contains(j.ReferenceType))
                .OrderBy(j => j.EntryDate)
                .ThenBy(j => j.Id)
                .ToListAsync(cancellationToken);

            var activeReferenceKeys = await _unitOfWork.Repository<JournalEntry>()
                .Query()
                .Where(j =>
                    j.CompanyId == companyId
                    && !j.IsDeleted
                    && j.ReferenceType != null
                    && j.ReferenceId != null
                    && transactionalTypes.Contains(j.ReferenceType))
                .Select(j => new { j.ReferenceType, j.ReferenceId })
                .ToListAsync(cancellationToken);

            var activeKeys = activeReferenceKeys
                .Select(x => $"{x.ReferenceType}:{x.ReferenceId}")
                .ToHashSet(StringComparer.Ordinal);

            var journalsRestored = 0;
            var invoicesPosted = 0;
            var billsApproved = 0;
            var receiptsRestored = 0;
            var bankTransactionsRestored = 0;
            var skippedDuplicates = 0;

            foreach (var journal in journals)
            {
                if (!journal.ReferenceId.HasValue || string.IsNullOrEmpty(journal.ReferenceType))
                {
                    skippedDuplicates++;
                    continue;
                }

                var key = $"{journal.ReferenceType}:{journal.ReferenceId}";
                if (activeKeys.Contains(key))
                {
                    skippedDuplicates++;
                    continue;
                }

                var linked = await RelinkCutoverDocumentAsync(
                    companyId,
                    journal,
                    now,
                    userName,
                    cancellationToken);

                if (!linked)
                {
                    skippedDuplicates++;
                    continue;
                }

                RestoreJournal(journal, now, userName);
                _unitOfWork.Repository<JournalEntry>().Update(journal);
                activeKeys.Add(key);
                journalsRestored++;

                switch (journal.ReferenceType)
                {
                    case ReferenceTypes.SalesInvoice:
                        invoicesPosted++;
                        break;
                    case ReferenceTypes.VendorBill:
                        billsApproved++;
                        break;
                    case ReferenceTypes.CustomerReceipt:
                        receiptsRestored++;
                        break;
                    case ReferenceTypes.BankTransaction:
                        bankTransactionsRestored++;
                        break;
                }
            }

            await RecalculateBankCustomerBalanceEffectsAsync(companyId, from, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var arBalance = await GetAccountBalanceAsync(companyId, AccountsReceivable, cancellationToken);
            var apBalance = await GetAccountBalanceAsync(companyId, AccountsPayable, cancellationToken);
            var (trialDebits, trialCredits) = await GetTrialBalanceTotalsAsync(companyId, cancellationToken);

            return new PostCutoverTransactionsResult(
                true,
                "Cutover transactions restored to the general ledger.",
                journalsRestored,
                invoicesPosted,
                billsApproved,
                receiptsRestored,
                bankTransactionsRestored,
                skippedDuplicates,
                arBalance,
                apBalance,
                trialDebits,
                trialCredits);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Post cutover transactions failed for company {CompanyId}", companyId);
            return new PostCutoverTransactionsResult(
                false,
                ex.Message,
                0,
                0,
                0,
                0,
                0,
                0,
                0m,
                0m,
                0m,
                0m);
        }
    }

    private const decimal KeptAsideOpeningFromQuickBooks = 60_000m;

    public async Task<TrialBalanceMismatchFixResult> FixTrialBalanceMismatchesAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "tb-mismatch-fix";

        var companyExists = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => c.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return new TrialBalanceMismatchFixResult(
                false,
                $"Company id {companyId} was not found.",
                0,
                0,
                false,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m);
        }

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var receiptJournalsFixed = await FixBrokenCustomerReceiptJournalsAsync(
                companyId,
                now,
                userName,
                cancellationToken);

            var duplicateBillsReversed = await ReverseDuplicateVendorBillsAsync(
                companyId,
                now,
                userName,
                cancellationToken);

            var keptAsideSet = await SetKeptAsideOpeningBalanceAsync(
                companyId,
                KeptAsideOpeningFromQuickBooks,
                now,
                userName,
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var itemsStockRecalculated = await RecalculateAllItemStockAsync(
                companyId,
                now,
                userName,
                cancellationToken);

            var cashBalance = await GetAccountBalanceAsync(companyId, CashInHand, cancellationToken);
            var arBalance = await GetAccountBalanceAsync(companyId, AccountsReceivable, cancellationToken);
            var inventoryBalance = await GetAccountBalanceAsync(companyId, InventoryAsset, cancellationToken);
            var apBalance = await GetAccountBalanceAsync(companyId, AccountsPayable, cancellationToken);
            var keptAsideBalance = await GetAccountBalanceAsync(companyId, KeptAside, cancellationToken);
            var (trialDebits, trialCredits) = await GetTrialBalanceTotalsAsync(companyId, cancellationToken);

            return new TrialBalanceMismatchFixResult(
                true,
                itemsStockRecalculated > 0
                    ? $"Trial balance mismatches corrected. Recalculated stock for {itemsStockRecalculated} item(s)."
                    : "Trial balance mismatches corrected.",
                receiptJournalsFixed,
                duplicateBillsReversed,
                keptAsideSet,
                cashBalance,
                arBalance,
                inventoryBalance,
                apBalance,
                keptAsideBalance,
                trialDebits,
                trialCredits);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Trial balance mismatch fix failed for company {CompanyId}", companyId);
            return new TrialBalanceMismatchFixResult(
                false,
                ex.Message,
                0,
                0,
                false,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m);
        }
    }

    private async Task<int> FixBrokenCustomerReceiptJournalsAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var fixedCount = 0;
        var receiptIds = await _unitOfWork.Repository<JournalEntry>()
            .Query()
            .Where(j =>
                j.CompanyId == companyId
                && !j.IsDeleted
                && j.ReferenceType == ReferenceTypes.CustomerReceipt
                && j.ReferenceId != null
                && j.Status == JournalStatus.Posted)
            .Select(j => j.ReferenceId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var receiptId in receiptIds)
        {
            var activeJournals = await _unitOfWork.Repository<JournalEntry>()
                .Query(asNoTracking: false)
                .Where(j =>
                    j.CompanyId == companyId
                    && !j.IsDeleted
                    && j.ReferenceType == ReferenceTypes.CustomerReceipt
                    && j.ReferenceId == receiptId)
                .ToListAsync(cancellationToken);

            var emptyActive = new List<JournalEntry>();
            var validActive = new List<JournalEntry>();

            foreach (var journal in activeJournals)
            {
                var lineCount = await _unitOfWork.Repository<JournalEntryLine>()
                    .Query()
                    .CountAsync(l => l.JournalEntryId == journal.Id, cancellationToken);

                if (lineCount == 0)
                {
                    emptyActive.Add(journal);
                }
                else
                {
                    validActive.Add(journal);
                }
            }

            if (emptyActive.Count == 0)
            {
                continue;
            }

            if (validActive.Count > 0)
            {
                foreach (var empty in emptyActive)
                {
                    SoftDeleteJournal(empty, now, userName);
                    _unitOfWork.Repository<JournalEntry>().Update(empty);
                    fixedCount++;
                }

                continue;
            }

            JournalEntry? journalToRestore = null;
            var deletedJournals = await _unitOfWork.Repository<JournalEntry>()
                .Query(asNoTracking: false)
                .IgnoreQueryFilters()
                .Where(j =>
                    j.CompanyId == companyId
                    && j.IsDeleted
                    && j.ReferenceType == ReferenceTypes.CustomerReceipt
                    && j.ReferenceId == receiptId
                    && j.Status == JournalStatus.Posted)
                .OrderByDescending(j => j.Id)
                .ToListAsync(cancellationToken);

            foreach (var deleted in deletedJournals)
            {
                var lineCount = await _unitOfWork.Repository<JournalEntryLine>()
                    .Query()
                    .CountAsync(l => l.JournalEntryId == deleted.Id, cancellationToken);

                if (lineCount > 0)
                {
                    journalToRestore = deleted;
                    break;
                }
            }

            foreach (var empty in emptyActive)
            {
                SoftDeleteJournal(empty, now, userName);
                _unitOfWork.Repository<JournalEntry>().Update(empty);
            }

            if (journalToRestore is not null)
            {
                RestoreJournal(journalToRestore, now, userName);
                _unitOfWork.Repository<JournalEntry>().Update(journalToRestore);
                fixedCount++;
                continue;
            }

            var receipt = await _unitOfWork.Repository<CustomerReceipt>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(
                    r => r.Id == receiptId && r.CompanyId == companyId && !r.IsDeleted,
                    cancellationToken);

            if (receipt is null)
            {
                continue;
            }

            var syncResult = await _customerGlPosting.SyncCustomerReceiptAsync(
                receipt,
                0m,
                null,
                receipt.PaymentMethod,
                receipt.ChequeBankType,
                cancellationToken: cancellationToken);

            if (syncResult.Success)
            {
                fixedCount++;
            }
        }

        return fixedCount;
    }

    private async Task<int> ReverseDuplicateVendorBillsAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var reversed = 0;
        var approvedBills = await _unitOfWork.Repository<VendorBill>()
            .Query(asNoTracking: false)
            .Where(b =>
                b.CompanyId == companyId
                && !b.IsDeleted
                && b.Status == BillStatus.Approved
                && b.RefNo != null
                && b.RefNo != string.Empty)
            .OrderBy(b => b.Id)
            .ToListAsync(cancellationToken);

        var duplicateGroups = approvedBills
            .GroupBy(b => (b.VendorId, RefNo: b.RefNo!.Trim(), b.NetAmount, b.BillDate.Date))
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            foreach (var duplicate in group.Skip(1))
            {
                await ReverseApprovedVendorBillAsync(duplicate, now, userName, cancellationToken);
                reversed++;
            }
        }

        return reversed;
    }

    private async Task ReverseApprovedVendorBillAsync(
        VendorBill bill,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        if (bill.JournalEntryId.HasValue)
        {
            var journal = await _unitOfWork.Repository<JournalEntry>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(j => j.Id == bill.JournalEntryId.Value, cancellationToken);

            if (journal is not null && !journal.IsDeleted)
            {
                SoftDeleteJournal(journal, now, userName);
                _unitOfWork.Repository<JournalEntry>().Update(journal);
            }
        }

        var inventoryTransactions = await _unitOfWork.Repository<InventoryTransaction>()
            .Query(asNoTracking: false)
            .Where(t => t.CompanyId == bill.CompanyId && t.ReferenceNo == bill.BillNumber)
            .ToListAsync(cancellationToken);

        var itemIds = inventoryTransactions.Select(t => t.ItemId).Distinct().ToList();
        foreach (var transaction in inventoryTransactions)
        {
            _unitOfWork.Repository<InventoryTransaction>().Remove(transaction);
        }

        bill.Status = BillStatus.Cancelled;
        bill.JournalEntryId = null;
        bill.UpdatedAt = now;
        bill.UpdatedBy = userName;
        _unitOfWork.Repository<VendorBill>().Update(bill);

        // Persist soft-deletes before recalculating; otherwise the SQL query still sees IsDeleted = 0.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await RecalculateItemStockFromTransactionsAsync(
            bill.CompanyId,
            itemIds,
            now,
            userName,
            cancellationToken);
    }

    public async Task<RecalculateItemStockResult> RecalculateItemStockAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var companyExists = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => c.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return new RecalculateItemStockResult(
                false,
                $"Company id {companyId} was not found.",
                0,
                0m,
                0m);
        }

        try
        {
            var now = DateTime.UtcNow;
            var userName = _currentUser.UserName ?? "recalculate-item-stock";
            var itemsUpdated = await RecalculateAllItemStockAsync(companyId, now, userName, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _itemCartonSyncService.SyncCompanyItemsAsync(companyId, cancellationToken);

            var sumItemStock = await _unitOfWork.Repository<Item>()
                .Query()
                .Where(i => i.CompanyId == companyId)
                .SumAsync(i => i.CurrentStock, cancellationToken);

            var sumTxnStock = await _unitOfWork.Repository<InventoryTransaction>()
                .Query()
                .Where(t => t.CompanyId == companyId)
                .SumAsync(
                    t => t.TransactionType == InventoryTransactionType.StockOut
                        ? -t.Quantity
                        : t.Quantity,
                    cancellationToken);

            return new RecalculateItemStockResult(
                true,
                itemsUpdated > 0
                    ? $"Recalculated stock for {itemsUpdated} item(s)."
                    : "All item stock quantities already match inventory transactions.",
                itemsUpdated,
                Math.Round(sumItemStock, 2),
                Math.Round(sumTxnStock, 2));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Item stock recalculation failed for company {CompanyId}", companyId);
            return new RecalculateItemStockResult(false, ex.Message, 0, 0m, 0m);
        }
    }

    private async Task<int> RecalculateAllItemStockAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var itemIds = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        if (itemIds.Count == 0)
        {
            return 0;
        }

        var items = await _unitOfWork.Repository<Item>()
            .Query(asNoTracking: false)
            .Where(i => i.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        var stockByItemId = await BuildStockByItemIdAsync(companyId, itemIds, cancellationToken);
        var itemsUpdated = 0;

        foreach (var item in items)
        {
            var stock = stockByItemId.GetValueOrDefault(item.Id);
            if (item.CurrentStock == stock)
            {
                continue;
            }

            item.CurrentStock = stock;
            item.UpdatedAt = now;
            item.UpdatedBy = userName;
            _unitOfWork.Repository<Item>().Update(item);
            itemsUpdated++;
        }

        return itemsUpdated;
    }

    private async Task RecalculateItemStockFromTransactionsAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return;
        }

        var items = await _unitOfWork.Repository<Item>()
            .Query(asNoTracking: false)
            .Where(i => i.CompanyId == companyId && itemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        var stockByItemId = await BuildStockByItemIdAsync(companyId, itemIds, cancellationToken);

        foreach (var item in items)
        {
            item.CurrentStock = stockByItemId.GetValueOrDefault(item.Id);
            item.UpdatedAt = now;
            item.UpdatedBy = userName;
            _unitOfWork.Repository<Item>().Update(item);
        }
    }

    private async Task<Dictionary<int, decimal>> BuildStockByItemIdAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId && itemIds.Contains(t.ItemId))
            .GroupBy(t => t.ItemId)
            .Select(g => new
            {
                ItemId = g.Key,
                Stock = g.Sum(t =>
                    t.TransactionType == InventoryTransactionType.StockOut
                        ? -t.Quantity
                        : t.TransactionType == InventoryTransactionType.Adjustment
                            ? t.Quantity
                            : t.Quantity)
            })
            .ToDictionaryAsync(x => x.ItemId, x => Math.Round(x.Stock, 2), cancellationToken);
    }

    private async Task<bool> SetKeptAsideOpeningBalanceAsync(
        int companyId,
        decimal openingBalance,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(
                a => a.CompanyId == companyId && a.AccountNumber == KeptAside && !a.IsDeleted,
                cancellationToken);

        if (account is null || account.OpeningBalance == openingBalance)
        {
            return false;
        }

        account.OpeningBalance = openingBalance;
        account.UpdatedAt = now;
        account.UpdatedBy = userName;
        _unitOfWork.Repository<ChartOfAccount>().Update(account);

        await RecalculateOpeningBalanceEquityPlugAsync(companyId, now, userName, cancellationToken);
        return true;
    }

    public async Task<TrialBalanceGapChaseResult> ChaseTrialBalanceGapAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "tb-gap-chase";

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var salesTaxPaymentsReclassified = await ReclassifyMispostedSalesTaxPaymentsAsync(
                companyId,
                now,
                userName,
                cancellationToken);

            var bankTransactionsReposted = await RepostCustomerBankWithdrawalsAsync(
                companyId,
                cancellationToken);

            await RecalculateOpeningBalanceEquityPlugAsync(companyId, now, userName, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var arBalance = await GetAccountBalanceAsync(companyId, AccountsReceivable, cancellationToken);
            var apBalance = await GetAccountBalanceAsync(companyId, AccountsPayable, cancellationToken);
            var taxBalance = await GetAccountBalanceAsync(companyId, SalesTaxPayable, cancellationToken);
            var asOf = new DateTime(2026, 6, 12);
            var (trialDebits, trialCredits) = await GetTrialBalanceTotalsAsync(companyId, asOf, cancellationToken);
            const decimal qbTotal = 1_102_428_325.69m;

            return new TrialBalanceGapChaseResult(
                true,
                "Trial balance gap chase completed.",
                salesTaxPaymentsReclassified,
                bankTransactionsReposted,
                arBalance,
                apBalance,
                taxBalance,
                trialDebits,
                trialCredits,
                qbTotal,
                qbTotal - trialDebits);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Trial balance gap chase failed for company {CompanyId}", companyId);
            return new TrialBalanceGapChaseResult(
                false,
                ex.Message,
                0,
                0,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m);
        }
    }

    private async Task RecalculateOpeningBalanceEquityPlugAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var accounts = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .Where(a => a.CompanyId == companyId && !a.IsDeleted)
            .ToListAsync(cancellationToken);

        var obeAccount = accounts.FirstOrDefault(a => a.AccountNumber == OpeningBalanceEquity);
        if (obeAccount is null)
        {
            return;
        }

        var plug = -accounts
            .Where(a => a.Id != obeAccount.Id)
            .Sum(a => a.OpeningBalance);

        obeAccount.OpeningBalance = Math.Round(plug, 2);
        obeAccount.UpdatedAt = now;
        obeAccount.UpdatedBy = userName;
        _unitOfWork.Repository<ChartOfAccount>().Update(obeAccount);
    }

    private async Task<int> ReclassifyMispostedSalesTaxPaymentsAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var apAccountId = await GetAccountIdAsync(companyId, AccountsPayable, cancellationToken);
        var taxAccountId = await GetAccountIdAsync(companyId, SalesTaxPayable, cancellationToken);
        if (!apAccountId.HasValue || !taxAccountId.HasValue)
        {
            return 0;
        }

        var transactions = await _unitOfWork.Repository<BankTransaction>()
            .Query(asNoTracking: false)
            .Where(bt =>
                bt.CompanyId == companyId
                && !bt.IsDeleted
                && bt.TransactionType == BankTransactionType.Withdrawal
                && bt.CounterChartOfAccountId == apAccountId.Value
                && bt.PartyName != null
                && (bt.PartyName.Contains("Sales Tax") || bt.PartyName.Contains("Used Tax")))
            .ToListAsync(cancellationToken);

        var fixedCount = 0;
        foreach (var transaction in transactions)
        {
            transaction.CounterChartOfAccountId = taxAccountId.Value;
            transaction.UpdatedAt = now;
            transaction.UpdatedBy = userName;
            _unitOfWork.Repository<BankTransaction>().Update(transaction);

            var postResult = await _bankGlPosting.PostBankTransactionAsync(transaction, cancellationToken);
            if (!postResult.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to repost sales tax payment {transaction.Id}: {postResult.Message}");
            }

            fixedCount++;
        }

        return fixedCount;
    }

    private async Task<int> RepostCustomerBankWithdrawalsAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var transactions = await _unitOfWork.Repository<BankTransaction>()
            .Query(asNoTracking: false)
            .Where(bt =>
                bt.CompanyId == companyId
                && !bt.IsDeleted
                && bt.TransactionType == BankTransactionType.Withdrawal
                && bt.CustomerId != null)
            .OrderBy(bt => bt.TransactionDate)
            .ThenBy(bt => bt.Id)
            .ToListAsync(cancellationToken);

        var reposted = 0;
        foreach (var transaction in transactions)
        {
            var postResult = await _bankGlPosting.PostBankTransactionAsync(transaction, cancellationToken);
            if (!postResult.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to repost customer bank transaction {transaction.Id}: {postResult.Message}");
            }

            reposted++;
        }

        return reposted;
    }

    public async Task<TrialBalanceCoaApplyResult> ApplyTrialBalanceCoaOpeningsAsync(
        int companyId,
        string trialBalanceFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(trialBalanceFilePath))
        {
            return new TrialBalanceCoaApplyResult(
                false,
                $"Trial balance file not found: {trialBalanceFilePath}",
                0,
                0,
                0,
                0m,
                0m);
        }

        var companyExists = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => c.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return new TrialBalanceCoaApplyResult(
                false,
                $"Company id {companyId} was not found.",
                0,
                0,
                0,
                0m,
                0m);
        }

        var parsedRows = QuickBooksReportCsvParser.ParseTrialBalanceCoaOpenings(trialBalanceFilePath);
        if (parsedRows.Count == 0)
        {
            return new TrialBalanceCoaApplyResult(
                false,
                "No account opening balances were found in the trial balance file.",
                0,
                0,
                0,
                0m,
                0m);
        }

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "trial-balance-import";
        var skipped = 0;
        var updated = 0;

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var accounts = await _unitOfWork.Repository<ChartOfAccount>()
                .Query(asNoTracking: false)
                .Where(a => a.CompanyId == companyId)
                .ToListAsync(cancellationToken);

            foreach (var account in accounts)
            {
                account.OpeningBalance = 0m;
                account.UpdatedAt = now;
                account.UpdatedBy = userName;
                _unitOfWork.Repository<ChartOfAccount>().Update(account);
            }

            foreach (var row in parsedRows)
            {
                var account = accounts.FirstOrDefault(a =>
                    string.Equals(a.AccountNumber, row.ErpAccountNumber, StringComparison.OrdinalIgnoreCase));

                if (account is null)
                {
                    skipped++;
                    _logger.LogWarning(
                        "Trial balance account {AccountNumber} not found in company {CompanyId}",
                        row.ErpAccountNumber,
                        companyId);
                    continue;
                }

                account.OpeningBalance = row.OpeningBalance;
                account.UpdatedAt = now;
                account.UpdatedBy = userName;
                _unitOfWork.Repository<ChartOfAccount>().Update(account);
                updated++;
            }

            foreach (var accountNumber in new[] { AccountsReceivable, AccountsPayable })
            {
                var controlAccount = accounts.FirstOrDefault(a =>
                    string.Equals(a.AccountNumber, accountNumber, StringComparison.OrdinalIgnoreCase));

                if (controlAccount is null)
                {
                    continue;
                }

                controlAccount.OpeningBalance = 0m;
                controlAccount.UpdatedAt = now;
                controlAccount.UpdatedBy = userName;
                _unitOfWork.Repository<ChartOfAccount>().Update(controlAccount);
            }

            var banksSynced = 0;
            var banks = await _unitOfWork.Repository<Bank>()
                .Query(asNoTracking: false)
                .Where(b => b.CompanyId == companyId && !b.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var bank in banks)
            {
                if (!bank.ChartOfAccountId.HasValue)
                {
                    continue;
                }

                var linkedAccount = accounts.FirstOrDefault(a => a.Id == bank.ChartOfAccountId.Value);
                if (linkedAccount is null)
                {
                    continue;
                }

                bank.OpeningBalance = linkedAccount.OpeningBalance;
                bank.CurrentBalance = linkedAccount.OpeningBalance;
                bank.UpdatedAt = now;
                bank.UpdatedBy = userName;
                _unitOfWork.Repository<Bank>().Update(bank);
                banksSynced++;
            }

            var journalsBackdated = await BackdateOpeningBalanceJournalsInternalAsync(
                companyId,
                new DateTime(2026, 5, 31),
                now,
                userName,
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var arBalance = await GetAccountBalanceAsync(companyId, AccountsReceivable, cancellationToken);
            var apBalance = await GetAccountBalanceAsync(companyId, AccountsPayable, cancellationToken);

            return new TrialBalanceCoaApplyResult(
                true,
                $"Trial balance opening balances applied for company {companyId}. Opening journals backdated: {journalsBackdated}.",
                updated,
                skipped,
                banksSynced,
                arBalance,
                apBalance);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Trial balance COA import failed for company {CompanyId}", companyId);
            return new TrialBalanceCoaApplyResult(
                false,
                ex.Message,
                0,
                0,
                0,
                0m,
                0m);
        }
    }

    public async Task<TrialBalanceCoaApplyResult> AlignTrialBalanceGlAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "trial-balance-align";

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var customerObTotal = await _unitOfWork.Repository<Customer>()
                .Query()
                .Where(c => c.CompanyId == companyId && !c.IsDeleted)
                .SumAsync(c => c.OpeningBalance, cancellationToken);

            var vendorObTotal = await _unitOfWork.Repository<Vendor>()
                .Query()
                .Where(v => v.CompanyId == companyId && !v.IsDeleted)
                .SumAsync(v => v.OpeningBalance, cancellationToken);

            var obJournals = await _unitOfWork.Repository<JournalEntry>()
                .Query(asNoTracking: false)
                .Where(j =>
                    j.CompanyId == companyId
                    && !j.IsDeleted
                    && (j.ReferenceType == ReferenceTypes.Customer || j.ReferenceType == ReferenceTypes.Vendor))
                .ToListAsync(cancellationToken);

            foreach (var journal in obJournals)
            {
                SoftDeleteJournal(journal, now, userName);
                _unitOfWork.Repository<JournalEntry>().Update(journal);
            }

            var accounts = await _unitOfWork.Repository<ChartOfAccount>()
                .Query(asNoTracking: false)
                .Where(a => a.CompanyId == companyId)
                .ToListAsync(cancellationToken);

            var arAccount = accounts.FirstOrDefault(a => a.AccountNumber == AccountsReceivable);
            var apAccount = accounts.FirstOrDefault(a => a.AccountNumber == AccountsPayable);
            var obeAccount = accounts.FirstOrDefault(a => a.AccountNumber == OpeningBalanceEquity);

            if (arAccount is null || apAccount is null || obeAccount is null)
            {
                throw new InvalidOperationException("AR, AP, or Opening Balance Equity account not found.");
            }

            arAccount.OpeningBalance = Math.Round(customerObTotal, 2);
            apAccount.OpeningBalance = Math.Round(-vendorObTotal, 2);

            var plug = -accounts
                .Where(a => a.Id != obeAccount.Id)
                .Sum(a => a.OpeningBalance);

            obeAccount.OpeningBalance = Math.Round(plug, 2);

            foreach (var account in new[] { arAccount, apAccount, obeAccount })
            {
                account.UpdatedAt = now;
                account.UpdatedBy = userName;
                _unitOfWork.Repository<ChartOfAccount>().Update(account);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var arBalance = await GetAccountBalanceAsync(companyId, AccountsReceivable, cancellationToken);
            var apBalance = await GetAccountBalanceAsync(companyId, AccountsPayable, cancellationToken);

            return new TrialBalanceCoaApplyResult(
                true,
                "Trial balance GL aligned: AR/AP on control accounts, subledger opening journals removed, OBE plugged.",
                3,
                0,
                0,
                arBalance,
                apBalance);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Trial balance GL alignment failed for company {CompanyId}", companyId);
            return new TrialBalanceCoaApplyResult(false, ex.Message, 0, 0, 0, 0m, 0m);
        }
    }

    public Task<int> BackdateOpeningBalanceJournalsAsync(
        int companyId,
        DateTime entryDate,
        CancellationToken cancellationToken = default) =>
        BackdateOpeningBalanceJournalsInternalAsync(companyId, entryDate, null, null, cancellationToken);

    private async Task<int> BackdateOpeningBalanceJournalsInternalAsync(
        int companyId,
        DateTime entryDate,
        DateTime? updatedAt,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        var effectiveDate = entryDate.Date;
        var now = updatedAt ?? DateTime.UtcNow;
        var user = updatedBy ?? _currentUser.UserName ?? "cutover-reconcile";

        var journals = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Where(j =>
                j.CompanyId == companyId
                && !j.IsDeleted
                && (j.ReferenceType == ReferenceTypes.Customer || j.ReferenceType == ReferenceTypes.Vendor))
            .ToListAsync(cancellationToken);

        foreach (var journal in journals)
        {
            journal.EntryDate = effectiveDate;
            journal.UpdatedAt = now;
            journal.UpdatedBy = user;
            _unitOfWork.Repository<JournalEntry>().Update(journal);
        }

        if (journals.Count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return journals.Count;
    }

    private async Task<int> RemapLegacyCoaJournalLinesAsync(int companyId, CancellationToken cancellationToken)
    {
        var remapped = 0;

        foreach (var (oldNumber, newNumber) in LegacyRemap)
        {
            var targetId = await GetAccountIdAsync(companyId, newNumber, cancellationToken);
            if (!targetId.HasValue)
            {
                continue;
            }

            var oldIds = await _unitOfWork.Repository<ChartOfAccount>()
                .Query()
                .Where(a => a.CompanyId == companyId && a.AccountNumber == oldNumber)
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);

            if (oldIds.Count == 0)
            {
                continue;
            }

            var lines = await _unitOfWork.Repository<JournalEntryLine>()
                .Query(asNoTracking: false)
                .Where(l => oldIds.Contains(l.ChartOfAccountId)
                            && l.JournalEntry.CompanyId == companyId
                            && !l.JournalEntry.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var line in lines)
            {
                if (line.ChartOfAccountId == targetId.Value)
                {
                    continue;
                }

                line.ChartOfAccountId = targetId.Value;
                _unitOfWork.Repository<JournalEntryLine>().Update(line);
                remapped++;
            }
        }

        return remapped;
    }

    private async Task<int> ConsolidateParentArAccountAsync(int companyId, CancellationToken cancellationToken)
    {
        var childAccountId = await GetAccountIdAsync(companyId, AccountsReceivable, cancellationToken);
        if (!childAccountId.HasValue)
        {
            return 0;
        }

        var parentAccountIds = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.AccountNumber == AccountsReceivableParent)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        if (parentAccountIds.Count == 0)
        {
            return 0;
        }

        var lines = await _unitOfWork.Repository<JournalEntryLine>()
            .Query(asNoTracking: false)
            .Where(l => parentAccountIds.Contains(l.ChartOfAccountId)
                        && l.JournalEntry.CompanyId == companyId
                        && !l.JournalEntry.IsDeleted)
            .ToListAsync(cancellationToken);

        var remapped = 0;
        foreach (var line in lines)
        {
            if (line.ChartOfAccountId == childAccountId.Value)
            {
                continue;
            }

            line.ChartOfAccountId = childAccountId.Value;
            _unitOfWork.Repository<JournalEntryLine>().Update(line);
            remapped++;
        }

        return remapped;
    }

    public async Task<int> ResyncSubledgerOpeningBalancesAsync(
        int companyId,
        DateTime entryDate,
        CancellationToken cancellationToken = default)
    {
        var customerCount = await ResyncCustomerOpeningBalanceJournalsAsync(companyId, entryDate, cancellationToken);
        var vendorCount = await ResyncVendorOpeningBalanceJournalsAsync(companyId, entryDate, cancellationToken);
        return customerCount + vendorCount;
    }

    private async Task<int> ResyncCustomerOpeningBalanceJournalsAsync(
        int companyId,
        DateTime? entryDate,
        CancellationToken cancellationToken)
    {
        var customers = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && !c.IsDeleted)
            .Select(c => new { c.Id, c.BuyerName, c.OpeningBalance })
            .ToListAsync(cancellationToken);

        var resynced = 0;
        foreach (var customer in customers)
        {
            var result = await _customerGlPosting.SyncCustomerOpeningBalanceAsync(
                customer.Id,
                customer.BuyerName,
                customer.OpeningBalance,
                entryDate,
                cancellationToken);

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    result.Message ?? $"Failed to resync opening balance for customer {customer.Id}.");
            }

            resynced++;
        }

        return resynced;
    }

    private async Task<int> ResyncVendorOpeningBalanceJournalsAsync(
        int companyId,
        DateTime? entryDate,
        CancellationToken cancellationToken)
    {
        var vendors = await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId && !v.IsDeleted)
            .Select(v => new { v.Id, v.VendorName, v.OpeningBalance })
            .ToListAsync(cancellationToken);

        var resynced = 0;
        foreach (var vendor in vendors)
        {
            var result = await _vendorGlPosting.SyncVendorOpeningBalanceAsync(
                vendor.Id,
                vendor.VendorName,
                vendor.OpeningBalance,
                entryDate,
                cancellationToken);

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    result.Message ?? $"Failed to resync opening balance for vendor {vendor.Id}.");
            }

            resynced++;
        }

        return resynced;
    }

    private async Task<int> SoftDeleteDuplicateCustomerReceiptJournalsAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var arAccountId = await GetAccountIdAsync(companyId, AccountsReceivable, cancellationToken);
        if (!arAccountId.HasValue)
        {
            return 0;
        }

        var removed = 0;
        var duplicateGroups = await _unitOfWork.Repository<JournalEntry>()
            .Query()
            .Where(j =>
                j.CompanyId == companyId
                && !j.IsDeleted
                && j.ReferenceType == ReferenceTypes.CustomerReceipt
                && j.ReferenceId != null
                && j.Status == JournalStatus.Posted)
            .GroupBy(j => j.ReferenceId!.Value)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync(cancellationToken);

        foreach (var receiptId in duplicateGroups)
        {
            var receipt = await _unitOfWork.Repository<CustomerReceipt>()
                .Query()
                .Where(r => r.Id == receiptId && r.CompanyId == companyId && !r.IsDeleted)
                .Select(r => new { r.Amount })
                .FirstOrDefaultAsync(cancellationToken);

            if (receipt is null)
            {
                continue;
            }

            var amount = Math.Round(receipt.Amount, 2);
            var journals = await _unitOfWork.Repository<JournalEntry>()
                .Query(asNoTracking: false)
                .Where(j =>
                    j.CompanyId == companyId
                    && !j.IsDeleted
                    && j.ReferenceType == ReferenceTypes.CustomerReceipt
                    && j.ReferenceId == receiptId
                    && j.Status == JournalStatus.Posted)
                .ToListAsync(cancellationToken);

            var keepId = await _unitOfWork.Repository<JournalEntryLine>()
                .Query()
                .Where(l =>
                    journals.Select(j => j.Id).Contains(l.JournalEntryId)
                    && l.ChartOfAccountId == arAccountId.Value
                    && l.Credit == amount)
                .Select(l => (int?)l.JournalEntryId)
                .FirstOrDefaultAsync(cancellationToken)
                ?? journals.OrderByDescending(j => j.Id).First().Id;

            foreach (var journal in journals)
            {
                if (journal.Id == keepId)
                {
                    continue;
                }

                SoftDeleteJournal(journal, now, userName);
                _unitOfWork.Repository<JournalEntry>().Update(journal);
                removed++;
            }
        }

        return removed;
    }

    private async Task<int> SoftDeleteDuplicateBankTransactionJournalsAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var removed = 0;

        var duplicateGroups = await _unitOfWork.Repository<JournalEntry>()
            .Query()
            .Where(j =>
                j.CompanyId == companyId
                && !j.IsDeleted
                && j.ReferenceType == ReferenceTypes.BankTransaction
                && j.ReferenceId != null
                && j.Status == JournalStatus.Posted)
            .GroupBy(j => j.ReferenceId!.Value)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync(cancellationToken);

        foreach (var transactionId in duplicateGroups)
        {
            var linkedJournalId = await _unitOfWork.Repository<BankTransaction>()
                .Query()
                .Where(bt => bt.Id == transactionId && bt.CompanyId == companyId && !bt.IsDeleted)
                .Select(bt => bt.JournalEntryId)
                .FirstOrDefaultAsync(cancellationToken);

            var journals = await _unitOfWork.Repository<JournalEntry>()
                .Query(asNoTracking: false)
                .Where(j =>
                    j.CompanyId == companyId
                    && !j.IsDeleted
                    && j.ReferenceType == ReferenceTypes.BankTransaction
                    && j.ReferenceId == transactionId
                    && j.Status == JournalStatus.Posted)
                .ToListAsync(cancellationToken);

            var keepId = linkedJournalId ?? journals.OrderByDescending(j => j.Id).First().Id;

            foreach (var journal in journals)
            {
                if (journal.Id == keepId)
                {
                    continue;
                }

                SoftDeleteJournal(journal, now, userName);
                _unitOfWork.Repository<JournalEntry>().Update(journal);
                removed++;
            }
        }

        return removed;
    }

    private async Task<int> PurgeDeletedJournalLinesAsync(CancellationToken cancellationToken)
    {
        var lines = await _unitOfWork.Repository<JournalEntryLine>()
            .Query(asNoTracking: false)
            .Where(l => l.JournalEntry.IsDeleted)
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            return 0;
        }

        _unitOfWork.Repository<JournalEntryLine>().RemoveRange(lines);
        return lines.Count;
    }

    private async Task<(int LinesAdded, int RevenueCreditsAdjusted)> FixCartageJournalLinesAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var revenueId = await GetAccountIdAsync(companyId, SalesRevenue, cancellationToken);
        var cartageId = await GetAccountIdAsync(companyId, CartagePayable, cancellationToken);
        if (!revenueId.HasValue || !cartageId.HasValue)
        {
            return (0, 0);
        }

        var cartageByJournal = new Dictionary<int, decimal>();

        var cartageInvoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(si =>
                si.CompanyId == companyId
                && si.JournalEntryId != null
                && si.Status == InvoiceStatus.Posted)
            .Include(si => si.Lines)
            .ThenInclude(l => l.Item)
            .ToListAsync(cancellationToken);

        foreach (var invoice in cartageInvoices)
        {
            if (!invoice.JournalEntryId.HasValue)
            {
                continue;
            }

            var cartageAmount = Math.Round(
                invoice.Lines
                    .Where(l => string.Equals(l.Item.ItemCode, CartageItemCode, StringComparison.OrdinalIgnoreCase))
                    .Sum(l => l.LineTotal),
                2);

            if (cartageAmount <= 0m)
            {
                continue;
            }

            cartageByJournal[invoice.JournalEntryId.Value] =
                cartageByJournal.GetValueOrDefault(invoice.JournalEntryId.Value) + cartageAmount;
        }

        var linesAdded = 0;
        var creditsAdjusted = 0;

        foreach (var row in cartageByJournal)
        {
            var journalId = row.Key;
            var amount = Math.Round(row.Value, 2);

            var hasCartageLine = await _unitOfWork.Repository<JournalEntryLine>()
                .Query()
                .AnyAsync(l =>
                    l.JournalEntryId == journalId
                    && l.ChartOfAccountId == cartageId.Value
                    && l.Memo == "Cartage Payable",
                    cancellationToken);

            if (hasCartageLine)
            {
                continue;
            }

            var revenueLine = await _unitOfWork.Repository<JournalEntryLine>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(l =>
                    l.JournalEntryId == journalId
                    && l.ChartOfAccountId == revenueId.Value
                    && l.Credit >= amount,
                    cancellationToken);

            if (revenueLine is not null)
            {
                revenueLine.Credit = Math.Round(revenueLine.Credit - amount, 2);
                _unitOfWork.Repository<JournalEntryLine>().Update(revenueLine);
                creditsAdjusted++;
            }

            await _unitOfWork.Repository<JournalEntryLine>().AddAsync(new JournalEntryLine
            {
                JournalEntryId = journalId,
                ChartOfAccountId = cartageId.Value,
                Debit = 0m,
                Credit = amount,
                Memo = "Cartage Payable"
            }, cancellationToken);
            linesAdded++;
        }

        return (linesAdded, creditsAdjusted);
    }

    private async Task<int> BackfillSalesInvoiceCogsLinesAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var cogsAccountId = await GetAccountIdAsync(companyId, CostOfGoodsSold, cancellationToken);
        if (!cogsAccountId.HasValue)
        {
            cogsAccountId = await _unitOfWork.Repository<ChartOfAccount>()
                .Query()
                .Where(a => a.CompanyId == companyId && a.IsActive && a.TypeId == CogsTypeId)
                .OrderBy(a => a.AccountNumber)
                .Select(a => (int?)a.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var inventoryAccountId = await GetAccountIdAsync(companyId, InventoryAsset, cancellationToken);
        if (!cogsAccountId.HasValue || !inventoryAccountId.HasValue)
        {
            return 0;
        }

        var cartageItemIds = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.ItemCode == CartageItemCode)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
        var cartageItemIdSet = cartageItemIds.ToHashSet();

        var invoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(si =>
                si.CompanyId == companyId
                && si.Status == InvoiceStatus.Posted
                && si.JournalEntryId != null
                && si.InvoiceType != InvoiceType.DebitNote)
            .Include(si => si.Lines)
            .ToListAsync(cancellationToken);

        if (invoices.Count == 0)
        {
            return 0;
        }

        var itemIds = invoices
            .SelectMany(i => i.Lines.Select(l => l.ItemId))
            .Distinct()
            .ToList();

        var items = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        var linesAdded = 0;

        foreach (var invoice in invoices)
        {
            if (!invoice.JournalEntryId.HasValue)
            {
                continue;
            }

            var journalId = invoice.JournalEntryId.Value;
            var journal = await _unitOfWork.Repository<JournalEntry>()
                .Query()
                .FirstOrDefaultAsync(j =>
                    j.Id == journalId
                    && j.CompanyId == companyId
                    && !j.IsDeleted,
                    cancellationToken);

            if (journal is null)
            {
                continue;
            }

            var alreadyHasCogs = await _unitOfWork.Repository<JournalEntryLine>()
                .Query()
                .AnyAsync(l =>
                    l.JournalEntryId == journalId
                    && l.ChartOfAccountId == cogsAccountId.Value,
                    cancellationToken);

            if (alreadyHasCogs)
            {
                continue;
            }

            decimal cogsAmount = 0m;
            foreach (var line in invoice.Lines)
            {
                if (cartageItemIdSet.Contains(line.ItemId))
                {
                    continue;
                }

                if (!items.TryGetValue(line.ItemId, out var item) || item.ItemType == ItemType.Service)
                {
                    continue;
                }

                var quantity = Math.Round(line.Quantity, 2);
                if (quantity <= 0m)
                {
                    continue;
                }

                cogsAmount += Math.Round(quantity * Math.Round(item.PurchaseRate, 2), 2);
            }

            cogsAmount = Math.Round(cogsAmount, 2);
            if (cogsAmount <= 0m)
            {
                continue;
            }

            if (invoice.InvoiceType == InvoiceType.CreditNote)
            {
                await _unitOfWork.Repository<JournalEntryLine>().AddAsync(new JournalEntryLine
                {
                    JournalEntryId = journalId,
                    ChartOfAccountId = inventoryAccountId.Value,
                    Debit = cogsAmount,
                    Credit = 0m,
                    Memo = "Inventory Asset"
                }, cancellationToken);

                await _unitOfWork.Repository<JournalEntryLine>().AddAsync(new JournalEntryLine
                {
                    JournalEntryId = journalId,
                    ChartOfAccountId = cogsAccountId.Value,
                    Debit = 0m,
                    Credit = cogsAmount,
                    Memo = "Cost of Goods Sold"
                }, cancellationToken);
            }
            else
            {
                await _unitOfWork.Repository<JournalEntryLine>().AddAsync(new JournalEntryLine
                {
                    JournalEntryId = journalId,
                    ChartOfAccountId = cogsAccountId.Value,
                    Debit = cogsAmount,
                    Credit = 0m,
                    Memo = "Cost of Goods Sold"
                }, cancellationToken);

                await _unitOfWork.Repository<JournalEntryLine>().AddAsync(new JournalEntryLine
                {
                    JournalEntryId = journalId,
                    ChartOfAccountId = inventoryAccountId.Value,
                    Debit = 0m,
                    Credit = cogsAmount,
                    Memo = "Inventory Asset"
                }, cancellationToken);
            }

            journal.UpdatedAt = now;
            journal.UpdatedBy = userName;
            _unitOfWork.Repository<JournalEntry>().Update(journal);
            linesAdded += 2;
        }

        return linesAdded;
    }

    private async Task<int> SoftDeleteDuplicateReferenceJournalsAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var removed = 0;

        var invoiceJournalIds = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(si => si.CompanyId == companyId && si.JournalEntryId != null)
            .Select(si => si.JournalEntryId!.Value)
            .ToListAsync(cancellationToken);
        var keepInvoice = invoiceJournalIds.ToHashSet();

        var duplicateInvoiceJournals = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Where(j =>
                j.CompanyId == companyId
                && !j.IsDeleted
                && j.ReferenceType == ReferenceTypes.SalesInvoice
                && j.Status == JournalStatus.Posted)
            .GroupBy(j => j.ReferenceId)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToListAsync(cancellationToken);

        foreach (var journal in duplicateInvoiceJournals)
        {
            if (keepInvoice.Contains(journal.Id))
            {
                continue;
            }

            SoftDeleteJournal(journal, now, userName);
            _unitOfWork.Repository<JournalEntry>().Update(journal);
            removed++;
        }

        return removed;
    }

    private async Task<int> SoftDeleteOrphanJournalsAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var removed = 0;

        var invoiceJournals = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Where(j =>
                j.CompanyId == companyId
                && !j.IsDeleted
                && j.ReferenceType == ReferenceTypes.SalesInvoice
                && j.ReferenceId != null)
            .ToListAsync(cancellationToken);

        foreach (var journal in invoiceJournals)
        {
            var invoice = await _unitOfWork.Repository<SalesInvoice>()
                .Query()
                .FirstOrDefaultAsync(si =>
                    si.Id == journal.ReferenceId
                    && si.CompanyId == companyId,
                    cancellationToken);

            if (invoice is null || invoice.JournalEntryId != journal.Id)
            {
                SoftDeleteJournal(journal, now, userName);
                _unitOfWork.Repository<JournalEntry>().Update(journal);
                removed++;
            }
        }

        var receiptJournals = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Where(j =>
                j.CompanyId == companyId
                && !j.IsDeleted
                && j.ReferenceType == ReferenceTypes.CustomerReceipt
                && j.ReferenceId != null)
            .ToListAsync(cancellationToken);

        foreach (var journal in receiptJournals)
        {
            var exists = await _unitOfWork.Repository<CustomerReceipt>()
                .Query()
                .AnyAsync(r => r.Id == journal.ReferenceId && r.CompanyId == companyId && !r.IsDeleted, cancellationToken);

            if (!exists)
            {
                SoftDeleteJournal(journal, now, userName);
                _unitOfWork.Repository<JournalEntry>().Update(journal);
                removed++;
            }
        }

        var billJournals = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Where(j =>
                j.CompanyId == companyId
                && !j.IsDeleted
                && j.ReferenceType == ReferenceTypes.VendorBill
                && j.ReferenceId != null)
            .ToListAsync(cancellationToken);

        foreach (var journal in billJournals)
        {
            var bill = await _unitOfWork.Repository<VendorBill>()
                .Query()
                .FirstOrDefaultAsync(b => b.Id == journal.ReferenceId && b.CompanyId == companyId, cancellationToken);

            if (bill is null || bill.JournalEntryId != journal.Id)
            {
                SoftDeleteJournal(journal, now, userName);
                _unitOfWork.Repository<JournalEntry>().Update(journal);
                removed++;
            }
        }

        var bankTxJournals = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Where(j =>
                j.CompanyId == companyId
                && !j.IsDeleted
                && j.ReferenceType == ReferenceTypes.BankTransaction
                && j.ReferenceId != null)
            .ToListAsync(cancellationToken);

        foreach (var journal in bankTxJournals)
        {
            var transaction = await _unitOfWork.Repository<BankTransaction>()
                .Query()
                .FirstOrDefaultAsync(bt => bt.Id == journal.ReferenceId && bt.CompanyId == companyId, cancellationToken);

            if (transaction is null || transaction.IsDeleted || transaction.JournalEntryId != journal.Id)
            {
                SoftDeleteJournal(journal, now, userName);
                _unitOfWork.Repository<JournalEntry>().Update(journal);
                removed++;
            }
        }

        return removed;
    }

    private static void SoftDeleteJournal(JournalEntry journal, DateTime now, string userName)
    {
        journal.IsDeleted = true;
        journal.DeletedAt = now;
        journal.DeletedBy = userName;
    }

    private static void RestoreJournal(JournalEntry journal, DateTime now, string userName)
    {
        journal.IsDeleted = false;
        journal.DeletedAt = null;
        journal.DeletedBy = null;
        journal.UpdatedAt = now;
        journal.UpdatedBy = userName;
    }

    private async Task<bool> RelinkCutoverDocumentAsync(
        int companyId,
        JournalEntry journal,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        if (!journal.ReferenceId.HasValue || string.IsNullOrEmpty(journal.ReferenceType))
        {
            return false;
        }

        switch (journal.ReferenceType)
        {
            case ReferenceTypes.SalesInvoice:
            {
                var invoice = await _unitOfWork.Repository<SalesInvoice>()
                    .Query(asNoTracking: false)
                    .FirstOrDefaultAsync(
                        i => i.Id == journal.ReferenceId.Value
                             && i.CompanyId == companyId
                             && !i.IsDeleted,
                        cancellationToken);

                if (invoice is null || invoice.Status != InvoiceStatus.Draft || invoice.JournalEntryId.HasValue)
                {
                    return false;
                }

                invoice.Status = InvoiceStatus.Posted;
                invoice.JournalEntryId = journal.Id;
                invoice.UpdatedAt = now;
                invoice.UpdatedBy = userName;
                _unitOfWork.Repository<SalesInvoice>().Update(invoice);
                return true;
            }

            case ReferenceTypes.VendorBill:
            {
                var bill = await _unitOfWork.Repository<VendorBill>()
                    .Query(asNoTracking: false)
                    .FirstOrDefaultAsync(
                        b => b.Id == journal.ReferenceId.Value
                             && b.CompanyId == companyId
                             && !b.IsDeleted,
                        cancellationToken);

                if (bill is null || bill.Status != BillStatus.Draft || bill.JournalEntryId.HasValue)
                {
                    return false;
                }

                bill.Status = BillStatus.Approved;
                bill.JournalEntryId = journal.Id;
                bill.UpdatedAt = now;
                bill.UpdatedBy = userName;
                _unitOfWork.Repository<VendorBill>().Update(bill);
                return true;
            }

            case ReferenceTypes.CustomerReceipt:
            {
                var receipt = await _unitOfWork.Repository<CustomerReceipt>()
                    .Query(asNoTracking: false)
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        r => r.Id == journal.ReferenceId.Value && r.CompanyId == companyId,
                        cancellationToken);

                if (receipt is null)
                {
                    return false;
                }

                if (receipt.IsDeleted)
                {
                    receipt.IsDeleted = false;
                    receipt.DeletedAt = null;
                    receipt.DeletedBy = null;
                    receipt.UpdatedAt = now;
                    receipt.UpdatedBy = userName;
                    _unitOfWork.Repository<CustomerReceipt>().Update(receipt);
                }

                return true;
            }

            case ReferenceTypes.BankTransaction:
            {
                var transaction = await _unitOfWork.Repository<BankTransaction>()
                    .Query(asNoTracking: false)
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        bt => bt.Id == journal.ReferenceId.Value && bt.CompanyId == companyId,
                        cancellationToken);

                if (transaction is null)
                {
                    return false;
                }

                if (transaction.IsDeleted)
                {
                    transaction.IsDeleted = false;
                    transaction.DeletedAt = null;
                    transaction.DeletedBy = null;
                }

                transaction.JournalEntryId = journal.Id;
                transaction.UpdatedAt = now;
                transaction.UpdatedBy = userName;
                _unitOfWork.Repository<BankTransaction>().Update(transaction);
                return true;
            }

            case ReferenceTypes.VendorPayment:
            {
                var payment = await _unitOfWork.Repository<VendorPayment>()
                    .Query(asNoTracking: false)
                    .FirstOrDefaultAsync(
                        p => p.Id == journal.ReferenceId.Value && p.CompanyId == companyId && !p.IsDeleted,
                        cancellationToken);

                return payment is not null;
            }

            default:
                return false;
        }
    }

    private async Task RecalculateBankCustomerBalanceEffectsAsync(
        int companyId,
        DateTime fromDate,
        CancellationToken cancellationToken)
    {
        var transactions = await _unitOfWork.Repository<BankTransaction>()
            .Query(asNoTracking: false)
            .Where(bt =>
                bt.CompanyId == companyId
                && !bt.IsDeleted
                && bt.TransactionDate >= fromDate
                && bt.TransactionType == BankTransactionType.Withdrawal
                && bt.CustomerId != null
                && bt.JournalEntryId != null)
            .OrderBy(bt => bt.TransactionDate)
            .ThenBy(bt => bt.Id)
            .ToListAsync(cancellationToken);

        foreach (var transaction in transactions)
        {
            var amount = Math.Round(transaction.Amount, 2);
            if (amount <= 0m)
            {
                transaction.CustomerBalanceEffect = 0m;
                _unitOfWork.Repository<BankTransaction>().Update(transaction);
                continue;
            }

            var outstanding = await GetCustomerOutstandingForBankTransactionAsync(
                transaction,
                cancellationToken);
            transaction.CustomerBalanceEffect = outstanding >= 0m ? -amount : amount;
            _unitOfWork.Repository<BankTransaction>().Update(transaction);
        }
    }

    private async Task<decimal> GetCustomerOutstandingForBankTransactionAsync(
        BankTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!transaction.CustomerId.HasValue)
        {
            return 0m;
        }

        var companyId = transaction.CompanyId;
        var customerId = transaction.CustomerId.Value;

        var openingBalance = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.Id == customerId && c.CompanyId == companyId)
            .Select(c => (decimal?)c.OpeningBalance)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

        var invoiceNet = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(si => si.CustomerId == customerId && si.Status == InvoiceStatus.Posted)
            .Select(si => new { si.InvoiceType, si.NetTotal })
            .ToListAsync(cancellationToken);

        var invoiceMovement = invoiceNet.Sum(i =>
            i.InvoiceType == InvoiceType.CreditNote ? -i.NetTotal : i.NetTotal);

        var receiptTotal = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r =>
                r.CustomerId == customerId
                && (r.PaymentMethod != PaymentMethod.Cheque
                    || (r.Status == CustomerReceiptStatus.Cleared && r.ClearedAt != null)))
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

        var chequeEffectTotal = await _unitOfWork.Repository<BankTransaction>()
            .Query()
            .Where(bt =>
                bt.CustomerId == customerId
                && bt.TransactionType == BankTransactionType.Withdrawal
                && !bt.IsDeleted
                && bt.Id != transaction.Id)
            .SumAsync(bt => (decimal?)bt.CustomerBalanceEffect, cancellationToken) ?? 0m;

        return Math.Round(openingBalance + invoiceMovement - receiptTotal + chequeEffectTotal, 2);
    }

    private Task<(decimal Debits, decimal Credits)> GetTrialBalanceTotalsAsync(
        int companyId,
        CancellationToken cancellationToken) =>
        GetTrialBalanceTotalsAsync(companyId, null, cancellationToken);

    private async Task<(decimal Debits, decimal Credits)> GetTrialBalanceTotalsAsync(
        int companyId,
        DateTime? asOfDate,
        CancellationToken cancellationToken)
    {
        var accounts = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.IsActive)
            .Select(a => new { a.Id, a.OpeningBalance })
            .ToListAsync(cancellationToken);

        decimal debits = 0m;
        decimal credits = 0m;

        foreach (var account in accounts)
        {
            if (account.OpeningBalance > 0m)
            {
                debits += account.OpeningBalance;
            }
            else if (account.OpeningBalance < 0m)
            {
                credits += Math.Abs(account.OpeningBalance);
            }
        }

        var journalQuery = _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l =>
                l.JournalEntry.CompanyId == companyId
                && l.JournalEntry.Status == JournalStatus.Posted
                && !l.JournalEntry.IsDeleted);

        if (asOfDate.HasValue)
        {
            var asOf = asOfDate.Value.Date;
            journalQuery = journalQuery.Where(l => l.JournalEntry.EntryDate <= asOf);
        }

        var lines = await journalQuery
            .Select(l => new { l.Debit, l.Credit })
            .ToListAsync(cancellationToken);

        debits += lines.Sum(l => l.Debit);
        credits += lines.Sum(l => l.Credit);

        return (Math.Round(debits, 2), Math.Round(credits, 2));
    }

    /// <summary>
    /// COGS should be driven by invoice postings; clear bogus import opening balances.
    /// </summary>
    private async Task ResetMisImportedCogsOpeningBalanceAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(
                a => a.CompanyId == companyId && a.AccountNumber == CostOfGoodsSold && a.IsActive,
                cancellationToken);

        if (account is null || account.OpeningBalance == 0m)
        {
            return;
        }

        var journalNet = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l =>
                l.ChartOfAccountId == account.Id
                && l.JournalEntry.CompanyId == companyId
                && l.JournalEntry.Status == JournalStatus.Posted
                && !l.JournalEntry.IsDeleted)
            .Select(l => l.Debit - l.Credit)
            .SumAsync(cancellationToken);

        if (Math.Abs(account.OpeningBalance) <= Math.Max(Math.Abs(journalNet) * 10m, 1_000_000m))
        {
            return;
        }

        account.OpeningBalance = 0m;
        account.UpdatedAt = now;
        account.UpdatedBy = userName;
        _unitOfWork.Repository<ChartOfAccount>().Update(account);
    }

    private async Task<decimal> GetAccountBalanceAsync(
        int companyId,
        string accountNumber,
        CancellationToken cancellationToken)
    {
        var accountId = await GetAccountIdAsync(companyId, accountNumber, cancellationToken);
        if (!accountId.HasValue)
        {
            return 0m;
        }

        var opening = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.Id == accountId.Value)
            .Select(a => a.OpeningBalance)
            .FirstOrDefaultAsync(cancellationToken);

        var totals = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l =>
                l.ChartOfAccountId == accountId.Value
                && l.JournalEntry.CompanyId == companyId
                && l.JournalEntry.Status == JournalStatus.Posted
                && !l.JournalEntry.IsDeleted)
            .Select(l => new { l.Debit, l.Credit })
            .ToListAsync(cancellationToken);

        return Math.Round(opening + totals.Sum(t => t.Debit - t.Credit), 2);
    }

    private async Task<int?> GetAccountIdAsync(
        int companyId,
        string accountNumber,
        CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.AccountNumber == accountNumber && a.IsActive)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
