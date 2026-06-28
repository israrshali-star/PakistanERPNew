using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common;
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
    // Option B: target closing balances (June collected) for company 3 sales tax sub-accounts.
    private const decimal Company3FurtherTaxTargetClosing = 2_864_761.24m;
    private const decimal Company3SalesTax18TargetClosing = 17_964_781.89m;
    private const decimal Company3SalesTaxParentTargetClosing = 20_829_543.13m;

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
        return await RepairHistoricalEntriesForCompanyAsync(companyId, cancellationToken);
    }

    public async Task<GlRepairResult> RepairHistoricalEntriesForCompanyAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "gl-repair";

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var legacyRemapped = await RemapLegacyCoaJournalLinesAsync(companyId, cancellationToken);
            var parentArConsolidated = await ConsolidateParentArAccountAsync(companyId, cancellationToken);
            var (cartageAdded, cartageAdjusted) = await FixCartageJournalLinesAsync(companyId, cancellationToken);
            var cogsAdded = await BackfillSalesInvoiceCogsLinesAsync(companyId, now, userName, cancellationToken);
            var furtherTaxSplit = TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId)
                ? 0
                : await BackfillSalesInvoiceFurtherTaxLinesAsync(companyId, now, userName, cancellationToken);
            var salesTaxSplit = await BackfillSalesTaxSplitGlAsync(companyId, now, userName, cancellationToken);
            var invertedArFixed = await FixInvertedSalesInvoiceArLinesAsync(companyId, now, userName, cancellationToken);
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

            var messageParts = new List<string> { "Historical GL entries repaired successfully." };
            if (furtherTaxSplit > 0)
            {
                messageParts.Add($"Split further tax on {furtherTaxSplit} posted invoice(s) to {FurtherTaxPayable}.");
            }

            if (salesTaxSplit > 0)
            {
                messageParts.Add($"Split sales tax on {salesTaxSplit} posted SN002 invoice(s) to {SalesTaxPayable18} (18%) and {FurtherTaxPayable} (4%); {SalesTaxPayable} shows the rolled-up total.");
            }

            if (invertedArFixed > 0)
            {
                messageParts.Add($"Corrected inverted AR on {invertedArFixed} posted sales invoice(s).");
            }

            return new GlRepairResult(
                true,
                string.Join(" ", messageParts),
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

    public async Task<(bool Success, string? Message, int InvoicesUpdated)> RepairCompany3SalesTaxGlAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        if (!TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId))
        {
            return (false, $"Sales tax GL split repair is only available for companies {string.Join(", ", TradeInvoiceLayout.SplitTaxGlCompanyIds)}.", 0);
        }

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "gl-repair";

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            await EnsureSalesTaxAccountHierarchyAsync(companyId, now, userName, cancellationToken);
            var updated = await BackfillSalesTaxSplitGlAsync(companyId, now, userName, cancellationToken);
            var invertedArFixed = await FixInvertedSalesInvoiceArLinesAsync(companyId, now, userName, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return (
                true,
                $"Updated sales tax GL on {updated} posted invoice(s). Corrected inverted AR on {invertedArFixed} invoice(s). {SalesTaxPayable18}=18%, {FurtherTaxPayable}=4%, {SalesTaxPayable} shows rolled-up total.",
                updated);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Sales tax GL repair failed for company {CompanyId}", companyId);
            return (false, ex.Message, 0);
        }
    }

    public async Task<(bool Success, string? Message, int InvoicesFixed, decimal AccountsReceivableBalance)> RepairAccountsReceivableGlAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "ar-gl-repair";

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            var fixedCount = await FixInvertedSalesInvoiceArLinesAsync(companyId, now, userName, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var arBalance = await GetAccountBalanceAsync(companyId, AccountsReceivable, cancellationToken);
            return (
                true,
                fixedCount > 0
                    ? $"Corrected inverted AR on {fixedCount} posted sales invoice(s)."
                    : "No inverted AR lines found on posted sales invoices.",
                fixedCount,
                arBalance);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "AR GL repair failed for company {CompanyId}", companyId);
            return (false, ex.Message, 0, 0m);
        }
    }

    public async Task<SalesTaxSubAccountRepairResult> RepairSalesTaxSubAccountTrialBalanceAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        if (!TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId))
        {
            return new SalesTaxSubAccountRepairResult(
                false,
                $"Split sales tax repair is only available for companies {string.Join(", ", TradeInvoiceLayout.SplitTaxGlCompanyIds)}.",
                0m,
                0m,
                0,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m);
        }

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "st-sub-repair";

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var parent = await _unitOfWork.Repository<ChartOfAccount>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(
                    a => a.CompanyId == companyId && a.AccountNumber == SalesTaxPayable && !a.IsDeleted,
                    cancellationToken);

            var furtherTaxAccount = await _unitOfWork.Repository<ChartOfAccount>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(
                    a => a.CompanyId == companyId && a.AccountNumber == FurtherTaxPayable && !a.IsDeleted,
                    cancellationToken);

            var salesTax18Account = await _unitOfWork.Repository<ChartOfAccount>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(
                    a => a.CompanyId == companyId && a.AccountNumber == SalesTaxPayable18 && !a.IsDeleted,
                    cancellationToken);

            if (parent is null || furtherTaxAccount is null || salesTax18Account is null)
            {
                return new SalesTaxSubAccountRepairResult(
                    false,
                    $"Sales tax accounts {SalesTaxPayable}, {FurtherTaxPayable}, or {SalesTaxPayable18} not found.",
                    0m,
                    0m,
                    0,
                    0m,
                    0m,
                    0m,
                    0m,
                    0m,
                    0m);
            }

            if (furtherTaxAccount.ParentAccountId != parent.Id)
            {
                furtherTaxAccount.ParentAccountId = parent.Id;
                furtherTaxAccount.UpdatedAt = now;
                furtherTaxAccount.UpdatedBy = userName;
                _unitOfWork.Repository<ChartOfAccount>().Update(furtherTaxAccount);
            }

            if (salesTax18Account.ParentAccountId != parent.Id)
            {
                salesTax18Account.ParentAccountId = parent.Id;
                salesTax18Account.UpdatedAt = now;
                salesTax18Account.UpdatedBy = userName;
                _unitOfWork.Repository<ChartOfAccount>().Update(salesTax18Account);
            }

            decimal furtherOpening;
            decimal salesTax18Opening;
            var bankPaymentsReposted = 0;

            if (companyId == TradeInvoiceLayout.TradeInvoiceCompanyId)
            {
                parent.OpeningBalance = 0m;
                parent.UpdatedAt = now;
                parent.UpdatedBy = userName;
                furtherTaxAccount.OpeningBalance = 0m;
                furtherTaxAccount.UpdatedAt = now;
                furtherTaxAccount.UpdatedBy = userName;
                salesTax18Account.OpeningBalance = 0m;
                salesTax18Account.UpdatedAt = now;
                salesTax18Account.UpdatedBy = userName;

                _unitOfWork.Repository<ChartOfAccount>().Update(parent);
                _unitOfWork.Repository<ChartOfAccount>().Update(furtherTaxAccount);
                _unitOfWork.Repository<ChartOfAccount>().Update(salesTax18Account);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                bankPaymentsReposted = await RepostSalesTaxBankPaymentsOnParentAsync(
                    companyId,
                    parent.Id,
                    cancellationToken);

                var furtherJournalMovement = await GetPostedJournalMovementAsync(
                    companyId,
                    furtherTaxAccount.Id,
                    cancellationToken);
                var salesTax18JournalMovement = await GetPostedJournalMovementAsync(
                    companyId,
                    salesTax18Account.Id,
                    cancellationToken);

                furtherOpening = Math.Round(
                    -Company3FurtherTaxTargetClosing - furtherJournalMovement,
                    2);
                salesTax18Opening = Math.Round(
                    -Company3SalesTax18TargetClosing - salesTax18JournalMovement,
                    2);
            }
            else
            {
                var totalOpening = Math.Round(
                    parent.OpeningBalance + furtherTaxAccount.OpeningBalance + salesTax18Account.OpeningBalance,
                    2);

                if (totalOpening <= 0m)
                {
                    totalOpening = Math.Round(
                        furtherTaxAccount.OpeningBalance + salesTax18Account.OpeningBalance,
                        2);
                }

                furtherOpening = Math.Round(totalOpening * 4m / 22m, 2);
                salesTax18Opening = Math.Round(totalOpening - furtherOpening, 2);

                parent.OpeningBalance = 0m;
                parent.UpdatedAt = now;
                parent.UpdatedBy = userName;
                furtherTaxAccount.OpeningBalance = furtherOpening;
                furtherTaxAccount.UpdatedAt = now;
                furtherTaxAccount.UpdatedBy = userName;
                salesTax18Account.OpeningBalance = salesTax18Opening;
                salesTax18Account.UpdatedAt = now;
                salesTax18Account.UpdatedBy = userName;

                _unitOfWork.Repository<ChartOfAccount>().Update(parent);
                _unitOfWork.Repository<ChartOfAccount>().Update(furtherTaxAccount);
                _unitOfWork.Repository<ChartOfAccount>().Update(salesTax18Account);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                bankPaymentsReposted = await RepostSalesTaxBankPaymentsOnParentAsync(
                    companyId,
                    parent.Id,
                    cancellationToken);
            }

            if (companyId == TradeInvoiceLayout.TradeInvoiceCompanyId)
            {
                parent.OpeningBalance = 0m;
                parent.UpdatedAt = now;
                parent.UpdatedBy = userName;
                furtherTaxAccount.OpeningBalance = furtherOpening;
                furtherTaxAccount.UpdatedAt = now;
                furtherTaxAccount.UpdatedBy = userName;
                salesTax18Account.OpeningBalance = salesTax18Opening;
                salesTax18Account.UpdatedAt = now;
                salesTax18Account.UpdatedBy = userName;

                _unitOfWork.Repository<ChartOfAccount>().Update(parent);
                _unitOfWork.Repository<ChartOfAccount>().Update(furtherTaxAccount);
                _unitOfWork.Repository<ChartOfAccount>().Update(salesTax18Account);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await RecalculateOpeningBalanceEquityPlugAsync(companyId, now, userName, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var (trialDebits, trialCredits) = await GetTrialBalanceTotalsAsync(companyId, cancellationToken);
            var obe = await GetAccountBalanceAsync(companyId, OpeningBalanceEquity, cancellationToken);

            return new SalesTaxSubAccountRepairResult(
                true,
                $"Sales tax openings set: {FurtherTaxPayable}={furtherOpening:N2}, {SalesTaxPayable18}={salesTax18Opening:N2}; parent {SalesTaxPayable} cleared; OBE replugged (Option B closings {Company3FurtherTaxTargetClosing:N2} / {Company3SalesTax18TargetClosing:N2}).",
                furtherOpening,
                salesTax18Opening,
                bankPaymentsReposted,
                await GetAccountBalanceAsync(companyId, SalesTaxPayable, cancellationToken),
                await GetAccountBalanceAsync(companyId, FurtherTaxPayable, cancellationToken),
                await GetAccountBalanceAsync(companyId, SalesTaxPayable18, cancellationToken),
                obe,
                trialDebits,
                trialCredits);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Sales tax sub-account repair failed for company {CompanyId}", companyId);
            return new SalesTaxSubAccountRepairResult(
                false,
                ex.Message,
                0m,
                0m,
                0,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m);
        }
    }

    private async Task<int> RepostSalesTaxBankPaymentsOnParentAsync(
        int companyId,
        int parentSalesTaxAccountId,
        CancellationToken cancellationToken)
    {
        var transactions = await _unitOfWork.Repository<BankTransaction>()
            .Query(asNoTracking: false)
            .Where(bt =>
                bt.CompanyId == companyId
                && !bt.IsDeleted
                && bt.TransactionType == BankTransactionType.Withdrawal
                && bt.CounterChartOfAccountId == parentSalesTaxAccountId)
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
                    postResult.Message ?? $"Failed to repost sales tax bank transaction {transaction.Id}.");
            }

            reposted++;
        }

        return reposted;
    }

    public async Task<OpeningBalanceEquityReplugResult> ReplugOpeningBalanceEquityAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "obe-replug";

        var obeAccount = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(
                a => a.CompanyId == companyId && a.AccountNumber == OpeningBalanceEquity,
                cancellationToken);

        if (obeAccount is null)
        {
            return new OpeningBalanceEquityReplugResult(
                false,
                $"Chart of account {OpeningBalanceEquity} (Opening Balance Equity) not found.",
                0m,
                0m,
                0m,
                0m);
        }

        var previous = obeAccount.OpeningBalance;

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            await ReplugOpeningBalanceEquityAsync(companyId, obeAccount.Id, now, userName, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var (trialDebits, trialCredits) = await GetTrialBalanceTotalsAsync(companyId, cancellationToken);
            var updated = await _unitOfWork.Repository<ChartOfAccount>()
                .Query()
                .Where(a => a.Id == obeAccount.Id)
                .Select(a => a.OpeningBalance)
                .FirstAsync(cancellationToken);

            return new OpeningBalanceEquityReplugResult(
                true,
                Math.Abs(trialDebits - trialCredits) < 0.01m
                    ? "Opening Balance Equity replugged. Trial balance is in balance."
                    : $"Opening Balance Equity replugged. Trial balance difference: {trialDebits - trialCredits:N2}.",
                previous,
                updated,
                trialDebits,
                trialCredits);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "OBE replug failed for company {CompanyId}", companyId);
            return new OpeningBalanceEquityReplugResult(false, ex.Message, previous, previous, 0m, 0m);
        }
    }

    public async Task<(bool Success, string? Message, decimal AmountMoved)> ReallocateSalesTaxOpeningBalanceAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        if (!TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId))
        {
            return (
                false,
                $"Sales tax opening balance relocation is only available for companies {string.Join(", ", TradeInvoiceLayout.SplitTaxGlCompanyIds)}.",
                0m);
        }

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "gl-repair";

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            await EnsureSalesTaxAccountHierarchyAsync(companyId, now, userName, cancellationToken);
            var amountMoved = await ReallocateSalesTaxOpeningBalanceCoreAsync(
                companyId,
                now,
                userName,
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return (
                true,
                amountMoved == 0m
                    ? $"No sales tax opening balance on {SalesTaxPayable} to move to {SalesTaxPayable18}."
                    : $"Moved sales tax opening balance {amountMoved:N2} from {SalesTaxPayable} to {SalesTaxPayable18}.",
                amountMoved);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Sales tax opening balance relocation failed for company {CompanyId}", companyId);
            return (false, ex.Message, 0m);
        }
    }

    public async Task<(bool Success, string? Message, decimal AmountTransferred)> TransferKeptAsideOpeningToSalesTax18Async(
        int companyId,
        decimal? transferAmount = null,
        CancellationToken cancellationToken = default)
    {
        var amountToTransfer = transferAmount ?? KeptAsideOpeningFromQuickBooks;
        if (amountToTransfer <= 0m)
        {
            return (false, "Transfer amount must be greater than zero.", 0m);
        }

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "gl-repair";

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            var transferred = await TransferKeptAsideOpeningToSalesTax18CoreAsync(
                companyId,
                amountToTransfer,
                now,
                userName,
                cancellationToken);
            await RecalculateOpeningBalanceEquityPlugAsync(companyId, now, userName, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return (
                true,
                transferred == 0m
                    ? $"No Kept Aside ({KeptAside}) opening balance to transfer to {SalesTaxPayable18}."
                    : $"Transferred Kept Aside B/F {transferred:N2} to {SalesTaxPayable18} (Sales Tax @ 18%).",
                transferred);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Kept Aside to sales tax 18% transfer failed for company {CompanyId}", companyId);
            return (false, ex.Message, 0m);
        }
    }

    public async Task<IReadOnlyList<(int CompanyId, bool Success, string? Message, int OpeningsFlipped, int PaymentsReposted)>> RepairSalesTaxPaymentGlForSplitTaxCompaniesAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<(int CompanyId, bool Success, string? Message, int OpeningsFlipped, int PaymentsReposted)>();

        foreach (var companyId in TradeInvoiceLayout.SplitTaxGlCompanyIds)
        {
            var result = await RepairSalesTaxPaymentGlAsync(companyId, cancellationToken);
            results.Add((companyId, result.Success, result.Message, result.OpeningsFlipped, result.PaymentsReposted));
        }

        return results;
    }

    public async Task<(bool Success, string? Message, int OpeningsFlipped, int PaymentsReposted, decimal SalesTax18Balance)> RepairSalesTaxPaymentGlAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        if (!TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId))
        {
            return (
                false,
                $"Sales tax payment GL repair is only available for companies {string.Join(", ", TradeInvoiceLayout.SplitTaxGlCompanyIds)}.",
                0,
                0,
                0m);
        }

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "sales-tax-payment-repair";
        var salesTaxNumbers = new[] { SalesTaxPayable, FurtherTaxPayable, SalesTaxPayable18 };

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var openingsFlipped = 0;
            var taxAccounts = await _unitOfWork.Repository<ChartOfAccount>()
                .Query(asNoTracking: false)
                .Where(a =>
                    a.CompanyId == companyId
                    && !a.IsDeleted
                    && salesTaxNumbers.Contains(a.AccountNumber)
                    && a.OpeningBalance > 0m)
                .ToListAsync(cancellationToken);

            foreach (var account in taxAccounts)
            {
                account.OpeningBalance = GlOpeningBalanceNormalizer.NormalizeForStorage(
                    account.OpeningBalance,
                    account.TypeId,
                    account.AccountNumber);
                account.UpdatedAt = now;
                account.UpdatedBy = userName;
                _unitOfWork.Repository<ChartOfAccount>().Update(account);
                openingsFlipped++;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var taxAccountIds = await _unitOfWork.Repository<ChartOfAccount>()
                .Query()
                .Where(a => a.CompanyId == companyId && !a.IsDeleted && salesTaxNumbers.Contains(a.AccountNumber))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);

            var payments = await _unitOfWork.Repository<BankTransaction>()
                .Query(asNoTracking: false)
                .Where(bt =>
                    bt.CompanyId == companyId
                    && !bt.IsDeleted
                    && bt.TransactionType == BankTransactionType.Withdrawal
                    && (
                        (bt.CounterChartOfAccountId.HasValue && taxAccountIds.Contains(bt.CounterChartOfAccountId.Value))
                        || (bt.PartyName != null && (bt.PartyName.Contains("Sales Tax") || bt.PartyName.Contains("Used Tax")))))
                .OrderBy(bt => bt.TransactionDate)
                .ThenBy(bt => bt.Id)
                .ToListAsync(cancellationToken);

            var reposted = 0;
            foreach (var payment in payments)
            {
                if (!await SalesTaxPaymentNeedsRepostAsync(payment, taxAccountIds, cancellationToken))
                {
                    continue;
                }

                var postResult = await _bankGlPosting.PostBankTransactionAsync(payment, cancellationToken);
                if (!postResult.Success)
                {
                    return (false, postResult.Message, openingsFlipped, reposted, 0m);
                }

                reposted++;
            }

            var openingsConsolidated = await ConsolidateFullyPaidSalesTaxOpeningsAsync(
                companyId,
                now,
                userName,
                cancellationToken);

            if (openingsFlipped > 0 || reposted > 0 || openingsConsolidated > 0)
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
                await RecalculateOpeningBalanceEquityPlugAsync(companyId, now, userName, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }

            var salesTax18 = await GetAccountBalanceAsync(companyId, SalesTaxPayable18, cancellationToken);
            var furtherTax = await GetAccountBalanceAsync(companyId, FurtherTaxPayable, cancellationToken);
            var parentTax = await GetAccountBalanceAsync(companyId, SalesTaxPayable, cancellationToken);

            return (
                true,
                $"Sales tax payment GL repaired. Openings flipped: {openingsFlipped}. Payments reposted: {reposted}. " +
                $"Openings consolidated: {openingsConsolidated}. " +
                $"{SalesTaxPayable18}={GlBalanceDisplay.NormalizeNetForDisplay(salesTax18, 2, SalesTaxPayable18):N2}, " +
                $"{FurtherTaxPayable}={GlBalanceDisplay.NormalizeNetForDisplay(furtherTax, 2, FurtherTaxPayable):N2}, " +
                $"{SalesTaxPayable}={GlBalanceDisplay.NormalizeNetForDisplay(parentTax, 2, SalesTaxPayable):N2}.",
                openingsFlipped,
                reposted,
                GlBalanceDisplay.NormalizeNetForDisplay(salesTax18, 2, SalesTaxPayable18));
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Sales tax payment GL repair failed for company {CompanyId}", companyId);
            return (false, ex.Message, 0, 0, 0m);
        }
    }

    private async Task<bool> SalesTaxPaymentNeedsRepostAsync(
        BankTransaction payment,
        IReadOnlyList<int> taxAccountIds,
        CancellationToken cancellationToken)
    {
        if (!payment.JournalEntryId.HasValue)
        {
            return true;
        }

        var taxLines = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l =>
                l.JournalEntryId == payment.JournalEntryId.Value
                && taxAccountIds.Contains(l.ChartOfAccountId))
            .ToListAsync(cancellationToken);

        if (taxLines.Count == 0)
        {
            return true;
        }

        return taxLines.Any(l => l.Debit > 0m);
    }

    private async Task<int> ConsolidateFullyPaidSalesTaxOpeningsAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var obeAccountId = await GetAccountIdAsync(companyId, OpeningBalanceEquity, cancellationToken);
        if (!obeAccountId.HasValue)
        {
            return 0;
        }

        const string consolidationDescription = "Sales tax opening consolidated after payment";
        var consolidated = 0;

        foreach (var accountNumber in new[] { FurtherTaxPayable, SalesTaxPayable18 })
        {
            var account = await _unitOfWork.Repository<ChartOfAccount>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(
                    a => a.CompanyId == companyId && a.AccountNumber == accountNumber && !a.IsDeleted,
                    cancellationToken);

            if (account is null || account.OpeningBalance == 0m)
            {
                continue;
            }

            var netBalance = await GetAccountBalanceAsync(companyId, accountNumber, cancellationToken);
            if (Math.Abs(netBalance) >= 0.01m)
            {
                continue;
            }

            var amount = Math.Round(Math.Abs(account.OpeningBalance), 2);
            if (amount <= 0m)
            {
                continue;
            }

            var hasConsolidationJournal = await _unitOfWork.Repository<JournalEntry>()
                .Query()
                .AnyAsync(
                    j => j.CompanyId == companyId
                         && !j.IsDeleted
                         && j.ReferenceType == ReferenceTypes.Manual
                         && j.ReferenceId == account.Id
                         && j.Description == consolidationDescription,
                    cancellationToken);

            if (!hasConsolidationJournal)
            {
                if (account.OpeningBalance < 0m)
                {
                    await PostManualRepairJournalAsync(
                        companyId,
                        new DateTime(2026, 5, 31),
                        consolidationDescription,
                        account.Id,
                        [
                            new JournalEntryLine
                            {
                                ChartOfAccountId = account.Id,
                                Debit = amount,
                                Credit = 0m,
                                Memo = $"{accountNumber} opening payable"
                            },
                            new JournalEntryLine
                            {
                                ChartOfAccountId = obeAccountId.Value,
                                Debit = 0m,
                                Credit = amount,
                                Memo = "Opening Balance Equity"
                            }
                        ],
                        cancellationToken);
                }
                else
                {
                    await PostManualRepairJournalAsync(
                        companyId,
                        new DateTime(2026, 5, 31),
                        consolidationDescription,
                        account.Id,
                        [
                            new JournalEntryLine
                            {
                                ChartOfAccountId = obeAccountId.Value,
                                Debit = amount,
                                Credit = 0m,
                                Memo = "Opening Balance Equity"
                            },
                            new JournalEntryLine
                            {
                                ChartOfAccountId = account.Id,
                                Debit = 0m,
                                Credit = amount,
                                Memo = $"{accountNumber} opening payable"
                            }
                        ],
                        cancellationToken);
                }
            }

            if (account.OpeningBalance != 0m)
            {
                account.OpeningBalance = 0m;
                account.UpdatedAt = now;
                account.UpdatedBy = userName;
                _unitOfWork.Repository<ChartOfAccount>().Update(account);
                consolidated++;
            }
        }

        return consolidated;
    }

    private async Task PostManualRepairJournalAsync(
        int companyId,
        DateTime entryDate,
        string description,
        int referenceId,
        IReadOnlyList<JournalEntryLine> lines,
        CancellationToken cancellationToken)
    {
        var entryNumber = await GenerateNextJournalEntryNumberAsync(companyId, cancellationToken);
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "sales-tax-payment-repair";

        var journalEntry = new JournalEntry
        {
            CompanyId = companyId,
            EntryNumber = entryNumber,
            EntryDate = entryDate.Date,
            Description = description,
            ReferenceType = ReferenceTypes.Manual,
            ReferenceId = referenceId,
            Status = JournalStatus.Posted,
            CreatedAt = now,
            CreatedBy = userName
        };

        await _unitOfWork.Repository<JournalEntry>().AddAsync(journalEntry, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var line in lines)
        {
            line.JournalEntryId = journalEntry.Id;
        }

        await _unitOfWork.Repository<JournalEntryLine>().AddRangeAsync(lines, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> GenerateNextJournalEntryNumberAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var prefix = AppConstants.JournalEntryNumberPrefix;
        var numbers = await _unitOfWork.Repository<JournalEntry>()
            .Query()
            .Where(j => j.CompanyId == companyId && j.EntryNumber.StartsWith(prefix))
            .Select(j => j.EntryNumber)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var number in numbers)
        {
            if (!number.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = number[prefix.Length..];
            if (int.TryParse(suffix, out var seq))
            {
                max = Math.Max(max, seq);
            }
        }

        return $"{prefix}{(max + 1):D4}";
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

            var keptAsideTransferred = await TransferKeptAsideOpeningToSalesTax18CoreAsync(
                companyId,
                KeptAsideOpeningFromQuickBooks,
                now,
                userName,
                cancellationToken);

            if (keptAsideTransferred > 0m)
            {
                await RecalculateOpeningBalanceEquityPlugAsync(companyId, now, userName, cancellationToken);
            }

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
            var salesTax18Balance = await GetAccountBalanceAsync(companyId, SalesTaxPayable18, cancellationToken);
            var (trialDebits, trialCredits) = await GetTrialBalanceTotalsAsync(companyId, cancellationToken);

            return new TrialBalanceMismatchFixResult(
                true,
                keptAsideTransferred > 0m
                    ? $"Trial balance mismatches corrected. Kept Aside B/F {keptAsideTransferred:N2} moved to {SalesTaxPayable18}."
                    : itemsStockRecalculated > 0
                        ? $"Trial balance mismatches corrected. Recalculated stock for {itemsStockRecalculated} item(s)."
                        : "Trial balance mismatches corrected.",
                receiptJournalsFixed,
                duplicateBillsReversed,
                keptAsideSet,
                keptAsideTransferred,
                cashBalance,
                arBalance,
                inventoryBalance,
                apBalance,
                keptAsideBalance,
                salesTax18Balance,
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

    public async Task<DeletedSalesInvoiceInventoryRepairResult> RepairDeletedSalesInvoiceInventoryAsync(
        int companyId,
        string? invoiceNumber = null,
        CancellationToken cancellationToken = default)
    {
        var companyExists = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => c.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return new DeletedSalesInvoiceInventoryRepairResult(
                false,
                $"Company id {companyId} was not found.",
                0,
                0,
                0);
        }

        try
        {
            var now = DateTime.UtcNow;
            var userName = _currentUser.UserName ?? "repair-deleted-invoice-inventory";

            var activePostedNumbers = await _unitOfWork.Repository<SalesInvoice>()
                .Query()
                .Where(i => i.CompanyId == companyId && i.Status == InvoiceStatus.Posted)
                .Select(i => i.InvoiceNumber)
                .ToListAsync(cancellationToken);
            var activePostedNumberSet = activePostedNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var inventoryQuery = _unitOfWork.Repository<InventoryTransaction>()
                .Query(asNoTracking: false)
                .Where(t => t.CompanyId == companyId
                            && t.ReferenceNo != null
                            && t.ReferenceNo.StartsWith(AppConstants.InvoiceNumberPrefix));

            if (!string.IsNullOrWhiteSpace(invoiceNumber))
            {
                inventoryQuery = inventoryQuery.Where(t => t.ReferenceNo == invoiceNumber.Trim());
            }

            var orphanInventoryTransactions = await inventoryQuery
                .Where(t => !activePostedNumberSet.Contains(t.ReferenceNo!))
                .ToListAsync(cancellationToken);

            var affectedItemIds = orphanInventoryTransactions
                .Select(t => t.ItemId)
                .Distinct()
                .ToList();

            foreach (var transaction in orphanInventoryTransactions)
            {
                _unitOfWork.Repository<InventoryTransaction>().Remove(transaction);
            }

            var salesInvoiceJournals = await _unitOfWork.Repository<JournalEntry>()
                .Query(asNoTracking: false)
                .Where(j => j.CompanyId == companyId
                            && !j.IsDeleted
                            && j.ReferenceType == ReferenceTypes.SalesInvoice
                            && j.ReferenceId != null)
                .ToListAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(invoiceNumber))
            {
                var invoiceIdsForNumber = await _unitOfWork.Repository<SalesInvoice>()
                    .Query(asNoTracking: false)
                    .IgnoreQueryFilters()
                    .Where(i => i.CompanyId == companyId && i.InvoiceNumber == invoiceNumber.Trim())
                    .Select(i => i.Id)
                    .ToListAsync(cancellationToken);

                salesInvoiceJournals = salesInvoiceJournals
                    .Where(j => invoiceIdsForNumber.Contains(j.ReferenceId!.Value))
                    .ToList();
            }

            var referencedInvoiceIds = salesInvoiceJournals
                .Select(j => j.ReferenceId!.Value)
                .Distinct()
                .ToList();

            var referencedInvoices = referencedInvoiceIds.Count == 0
                ? new Dictionary<int, (bool IsDeleted, InvoiceStatus Status, int? JournalEntryId)>()
                : await _unitOfWork.Repository<SalesInvoice>()
                    .Query(asNoTracking: false)
                    .IgnoreQueryFilters()
                    .Where(i => i.CompanyId == companyId && referencedInvoiceIds.Contains(i.Id))
                    .Select(i => new { i.Id, i.IsDeleted, i.Status, i.JournalEntryId })
                    .ToDictionaryAsync(
                        i => i.Id,
                        i => (i.IsDeleted, i.Status, i.JournalEntryId),
                        cancellationToken);

            var orphanJournalEntries = new List<JournalEntry>();
            foreach (var journal in salesInvoiceJournals)
            {
                if (!referencedInvoices.TryGetValue(journal.ReferenceId!.Value, out var invoiceState))
                {
                    orphanJournalEntries.Add(journal);
                    continue;
                }

                if (invoiceState.IsDeleted
                    || invoiceState.Status != InvoiceStatus.Posted
                    || invoiceState.JournalEntryId != journal.Id)
                {
                    orphanJournalEntries.Add(journal);
                }
            }

            foreach (var journal in orphanJournalEntries)
            {
                journal.IsDeleted = true;
                journal.DeletedAt = now;
                journal.DeletedBy = userName;
                _unitOfWork.Repository<JournalEntry>().Update(journal);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var itemsUpdated = 0;
            if (affectedItemIds.Count > 0)
            {
                itemsUpdated = await RecalculateAllItemStockAsync(companyId, now, userName, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _itemCartonSyncService.SyncItemsAsync(companyId, affectedItemIds, cancellationToken);
            }

            return new DeletedSalesInvoiceInventoryRepairResult(
                true,
                orphanInventoryTransactions.Count > 0 || orphanJournalEntries.Count > 0
                    ? $"Removed {orphanInventoryTransactions.Count} orphan inventory transaction(s) and {orphanJournalEntries.Count} orphan journal entry(s)."
                    : "No orphan sales invoice inventory or journal entries found.",
                orphanInventoryTransactions.Count,
                orphanJournalEntries.Count,
                itemsUpdated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deleted sales invoice inventory repair failed for company {CompanyId}", companyId);
            return new DeletedSalesInvoiceInventoryRepairResult(false, ex.Message, 0, 0, 0);
        }
    }

    public async Task<SalesInvoiceCogsRepairResult> RepairUnderstatedSalesInvoiceCogsAsync(
        int companyId,
        string? invoiceNumber = null,
        CancellationToken cancellationToken = default)
    {
        var companyExists = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => c.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return new SalesInvoiceCogsRepairResult(
                false,
                $"Company id {companyId} was not found.",
                0,
                0,
                0m);
        }

        try
        {
            var now = DateTime.UtcNow;
            var userName = _currentUser.UserName ?? "repair-invoice-cogs";

            var openingTransactions = await _unitOfWork.Repository<InventoryTransaction>()
                .Query(asNoTracking: false)
                .Where(t => t.CompanyId == companyId
                            && t.UnitCost <= 0m
                            && (t.ReferenceNo == AppConstants.OpeningStockBillNumber
                                || t.TransactionType == InventoryTransactionType.Opening))
                .ToListAsync(cancellationToken);

            var openingItemIds = openingTransactions.Select(t => t.ItemId).Distinct().ToList();
            var openingPurchaseRates = openingItemIds.Count == 0
                ? new Dictionary<int, decimal>()
                : await _unitOfWork.Repository<Item>()
                    .Query()
                    .Where(i => i.CompanyId == companyId && openingItemIds.Contains(i.Id) && i.PurchaseRate > 0m)
                    .ToDictionaryAsync(i => i.Id, i => i.PurchaseRate, cancellationToken);

            var openingTransactionsFixed = 0;
            foreach (var transaction in openingTransactions)
            {
                if (!openingPurchaseRates.TryGetValue(transaction.ItemId, out var purchaseRate))
                {
                    continue;
                }

                var unitCost = Math.Round(purchaseRate, 2);
                var totalCost = Math.Round(transaction.Quantity * unitCost, 2);
                if (transaction.UnitCost == unitCost && transaction.TotalCost == totalCost)
                {
                    continue;
                }

                transaction.UnitCost = unitCost;
                transaction.TotalCost = totalCost;
                transaction.UpdatedAt = now;
                transaction.UpdatedBy = userName;
                _unitOfWork.Repository<InventoryTransaction>().Update(transaction);
                openingTransactionsFixed++;
            }

            var cogsAccountId = await GetAccountIdAsync(companyId, CostOfGoodsSold, cancellationToken);
            var inventoryAccountId = await GetAccountIdAsync(companyId, InventoryAsset, cancellationToken);
            if (!cogsAccountId.HasValue || !inventoryAccountId.HasValue)
            {
                return new SalesInvoiceCogsRepairResult(
                    false,
                    "COGS or inventory asset account was not found.",
                    openingTransactionsFixed,
                    0,
                    0m);
            }

            var cartageItemIds = await _unitOfWork.Repository<Item>()
                .Query()
                .Where(i => i.CompanyId == companyId && i.ItemCode == CartageItemCode)
                .Select(i => i.Id)
                .ToListAsync(cancellationToken);
            var cartageItemIdSet = cartageItemIds.ToHashSet();

            var invoiceQuery = _unitOfWork.Repository<SalesInvoice>()
                .Query(asNoTracking: false)
                .Include(i => i.Lines)
                .Where(i => i.CompanyId == companyId
                            && i.Status == InvoiceStatus.Posted
                            && i.JournalEntryId != null
                            && i.InvoiceType != InvoiceType.DebitNote);

            if (!string.IsNullOrWhiteSpace(invoiceNumber))
            {
                invoiceQuery = invoiceQuery.Where(i => i.InvoiceNumber == invoiceNumber.Trim());
            }

            var invoices = await invoiceQuery.ToListAsync(cancellationToken);
            if (invoices.Count == 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return new SalesInvoiceCogsRepairResult(
                    true,
                    openingTransactionsFixed > 0
                        ? $"Updated {openingTransactionsFixed} opening stock transaction(s). No posted invoices to adjust."
                        : "No opening stock or invoice COGS adjustments were needed.",
                    openingTransactionsFixed,
                    0,
                    0m);
            }

            var itemIds = invoices
                .SelectMany(i => i.Lines.Select(l => l.ItemId))
                .Distinct()
                .ToList();

            var items = await _unitOfWork.Repository<Item>()
                .Query()
                .Where(i => i.CompanyId == companyId && itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, cancellationToken);

            var stackLotRates = await BuildStackLotPurchaseRatesForRepairAsync(companyId, itemIds, cancellationToken);

            var invoicesAdjusted = 0;
            var totalCogsAdjusted = 0m;

            foreach (var invoice in invoices.OrderBy(i => i.InvoiceDate).ThenBy(i => i.Id))
            {
                if (!invoice.JournalEntryId.HasValue)
                {
                    continue;
                }

                var journalLines = await _unitOfWork.Repository<JournalEntryLine>()
                    .Query(asNoTracking: false)
                    .Where(l => l.JournalEntryId == invoice.JournalEntryId.Value)
                    .ToListAsync(cancellationToken);

                var cogsLine = journalLines.FirstOrDefault(l => l.ChartOfAccountId == cogsAccountId.Value);
                var inventoryLine = journalLines.FirstOrDefault(l => l.ChartOfAccountId == inventoryAccountId.Value);
                if (cogsLine is null || inventoryLine is null)
                {
                    continue;
                }

                decimal expectedCogs = 0m;
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

                    var stackNo = string.IsNullOrWhiteSpace(line.StackNo) ? null : line.StackNo.Trim();
                    var lotNo = string.IsNullOrWhiteSpace(line.LotNo) ? null : line.LotNo.Trim();
                    var rate = ResolveStackLotRate(stackLotRates, item.Id, stackNo, lotNo, item.PurchaseRate);
                    var lineCost = Math.Round(quantity * rate, 2);
                    expectedCogs += lineCost;

                    var inventoryTransactions = await _unitOfWork.Repository<InventoryTransaction>()
                        .Query(asNoTracking: false)
                        .Where(t => t.CompanyId == companyId
                                    && t.ReferenceNo == invoice.InvoiceNumber
                                    && t.ItemId == line.ItemId
                                    && t.TransactionType == InventoryTransactionType.StockOut
                                    && t.Quantity == quantity)
                        .ToListAsync(cancellationToken);

                    var inventoryTransaction = inventoryTransactions.FirstOrDefault(t =>
                        string.Equals(t.StackNo ?? string.Empty, stackNo ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(t.LotNo ?? string.Empty, lotNo ?? string.Empty, StringComparison.OrdinalIgnoreCase));

                    if (inventoryTransaction is not null
                        && (inventoryTransaction.UnitCost != rate || inventoryTransaction.TotalCost != lineCost))
                    {
                        inventoryTransaction.UnitCost = rate;
                        inventoryTransaction.TotalCost = lineCost;
                        inventoryTransaction.UpdatedAt = now;
                        inventoryTransaction.UpdatedBy = userName;
                        _unitOfWork.Repository<InventoryTransaction>().Update(inventoryTransaction);
                    }
                }

                expectedCogs = Math.Round(expectedCogs, 2);
                var actualCogs = Math.Round(cogsLine.Debit - cogsLine.Credit, 2);
                var delta = Math.Round(expectedCogs - actualCogs, 2);
                if (Math.Abs(delta) < 0.01m)
                {
                    continue;
                }

                if (invoice.InvoiceType == InvoiceType.CreditNote)
                {
                    cogsLine.Credit = Math.Round(cogsLine.Credit + delta, 2);
                    inventoryLine.Debit = Math.Round(inventoryLine.Debit + delta, 2);
                }
                else
                {
                    cogsLine.Debit = Math.Round(cogsLine.Debit + delta, 2);
                    inventoryLine.Credit = Math.Round(inventoryLine.Credit + delta, 2);
                }

                cogsLine.Memo ??= "Cost of Goods Sold";
                inventoryLine.Memo ??= "Inventory Asset";
                _unitOfWork.Repository<JournalEntryLine>().Update(cogsLine);
                _unitOfWork.Repository<JournalEntryLine>().Update(inventoryLine);

                invoicesAdjusted++;
                totalCogsAdjusted += delta;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new SalesInvoiceCogsRepairResult(
                true,
                invoicesAdjusted > 0 || openingTransactionsFixed > 0
                    ? $"Fixed {openingTransactionsFixed} opening stock transaction(s) and adjusted COGS on {invoicesAdjusted} invoice(s) by {totalCogsAdjusted:N2}."
                    : "Opening stock and invoice COGS already match expected inventory cost.",
                openingTransactionsFixed,
                invoicesAdjusted,
                totalCogsAdjusted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sales invoice COGS repair failed for company {CompanyId}", companyId);
            return new SalesInvoiceCogsRepairResult(false, ex.Message, 0, 0, 0m);
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

    private async Task<decimal> TransferKeptAsideOpeningToSalesTax18CoreAsync(
        int companyId,
        decimal transferAmount,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        if (transferAmount <= 0m)
        {
            return 0m;
        }

        var keptAsideAccount = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(
                a => a.CompanyId == companyId && a.AccountNumber == KeptAside && !a.IsDeleted,
                cancellationToken);

        if (keptAsideAccount is null || keptAsideAccount.OpeningBalance <= 0m)
        {
            return 0m;
        }

        if (TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId))
        {
            await EnsureSalesTaxAccountHierarchyAsync(companyId, now, userName, cancellationToken);
        }

        var salesTax18AccountId = await EnsureSalesTaxPayable18AccountAsync(
            companyId,
            now,
            userName,
            cancellationToken);

        if (!salesTax18AccountId.HasValue)
        {
            return 0m;
        }

        var salesTax18Account = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstAsync(a => a.Id == salesTax18AccountId.Value, cancellationToken);

        var amount = Math.Min(keptAsideAccount.OpeningBalance, transferAmount);
        if (amount <= 0m)
        {
            return 0m;
        }

        keptAsideAccount.OpeningBalance = Math.Round(keptAsideAccount.OpeningBalance - amount, 2);
        keptAsideAccount.UpdatedAt = now;
        keptAsideAccount.UpdatedBy = userName;

        salesTax18Account.OpeningBalance = Math.Round(salesTax18Account.OpeningBalance + amount, 2);
        salesTax18Account.UpdatedAt = now;
        salesTax18Account.UpdatedBy = userName;

        _unitOfWork.Repository<ChartOfAccount>().Update(keptAsideAccount);
        _unitOfWork.Repository<ChartOfAccount>().Update(salesTax18Account);

        return amount;
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
        var obeAccountId = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.AccountNumber == OpeningBalanceEquity && !a.IsDeleted)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!obeAccountId.HasValue)
        {
            return;
        }

        await ReplugOpeningBalanceEquityAsync(companyId, obeAccountId.Value, now, userName, cancellationToken);
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
                var erpAccountNumber = row.ErpAccountNumber;
                if (TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId)
                    && string.Equals(erpAccountNumber, SalesTaxPayable, StringComparison.OrdinalIgnoreCase))
                {
                    erpAccountNumber = SalesTaxPayable18;
                }

                var account = accounts.FirstOrDefault(a =>
                    string.Equals(a.AccountNumber, erpAccountNumber, StringComparison.OrdinalIgnoreCase));

                if (account is null)
                {
                    skipped++;
                    _logger.LogWarning(
                        "Trial balance account {AccountNumber} not found in company {CompanyId}",
                        erpAccountNumber,
                        companyId);
                    continue;
                }

                account.OpeningBalance = GlOpeningBalanceNormalizer.NormalizeForStorage(
                    row.OpeningBalance,
                    account.TypeId,
                    account.AccountNumber);
                account.UpdatedAt = now;
                account.UpdatedBy = userName;
                _unitOfWork.Repository<ChartOfAccount>().Update(account);
                updated++;
            }

            foreach (var accountNumber in new[] { SalesTaxPayable })
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

            await ReallocateSalesTaxOpeningBalanceCoreAsync(companyId, now, userName, cancellationToken);

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
                .Select(v => v.OpeningBalance)
                .ToListAsync(cancellationToken);

            var apOpeningTotal = vendorObTotal
                .Sum(QuickBooksSubledgerBalance.NormalizeVendorOpeningForControlAccount);

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
            apAccount.OpeningBalance = Math.Round(apOpeningTotal, 2);

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

    private async Task<int> BackfillSalesInvoiceFurtherTaxLinesAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var taxAccountId = await GetAccountIdAsync(companyId, SalesTaxPayable, cancellationToken);
        if (!taxAccountId.HasValue)
        {
            return 0;
        }

        var furtherTaxAccountId = await EnsureFurtherTaxPayableAccountAsync(
            companyId,
            now,
            userName,
            cancellationToken);
        if (!furtherTaxAccountId.HasValue)
        {
            return 0;
        }

        var invoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query(asNoTracking: false)
            .Where(si =>
                si.CompanyId == companyId
                && si.Status == InvoiceStatus.Posted
                && si.JournalEntryId != null
                && si.FurtherTax > 0m)
            .ToListAsync(cancellationToken);

        if (invoices.Count == 0)
        {
            return 0;
        }

        var adjusted = 0;

        foreach (var invoice in invoices)
        {
            var journalId = invoice.JournalEntryId!.Value;
            var furtherTaxAmount = Math.Round(invoice.FurtherTax, 2);
            if (furtherTaxAmount <= 0m)
            {
                continue;
            }

            var alreadySplit = await _unitOfWork.Repository<JournalEntryLine>()
                .Query()
                .AnyAsync(l =>
                    l.JournalEntryId == journalId
                    && l.ChartOfAccountId == furtherTaxAccountId.Value,
                    cancellationToken);

            if (alreadySplit)
            {
                continue;
            }

            var taxLines = await _unitOfWork.Repository<JournalEntryLine>()
                .Query(asNoTracking: false)
                .Where(l =>
                    l.JournalEntryId == journalId
                    && l.ChartOfAccountId == taxAccountId.Value)
                .ToListAsync(cancellationToken);

            if (taxLines.Count == 0)
            {
                continue;
            }

            var isCreditNote = invoice.InvoiceType == InvoiceType.CreditNote;
            var taxLine = taxLines
                .OrderByDescending(l => isCreditNote ? l.Debit : l.Credit)
                .First();

            if (isCreditNote)
            {
                if (taxLine.Debit < furtherTaxAmount)
                {
                    continue;
                }

                taxLine.Debit = Math.Round(taxLine.Debit - furtherTaxAmount, 2);
                await _unitOfWork.Repository<JournalEntryLine>().AddAsync(new JournalEntryLine
                {
                    JournalEntryId = journalId,
                    ChartOfAccountId = furtherTaxAccountId.Value,
                    Debit = furtherTaxAmount,
                    Credit = 0m,
                    Memo = "Further Tax Payable"
                }, cancellationToken);
            }
            else
            {
                if (taxLine.Credit < furtherTaxAmount)
                {
                    continue;
                }

                taxLine.Credit = Math.Round(taxLine.Credit - furtherTaxAmount, 2);
                await _unitOfWork.Repository<JournalEntryLine>().AddAsync(new JournalEntryLine
                {
                    JournalEntryId = journalId,
                    ChartOfAccountId = furtherTaxAccountId.Value,
                    Debit = 0m,
                    Credit = furtherTaxAmount,
                    Memo = "Further Tax Payable"
                }, cancellationToken);
            }

            _unitOfWork.Repository<JournalEntryLine>().Update(taxLine);

            var journal = await _unitOfWork.Repository<JournalEntry>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(j => j.Id == journalId && j.CompanyId == companyId, cancellationToken);

            if (journal is not null)
            {
                journal.UpdatedAt = now;
                journal.UpdatedBy = userName;
                _unitOfWork.Repository<JournalEntry>().Update(journal);
            }

            adjusted++;
        }

        return adjusted;
    }

    private async Task<int> BackfillSalesTaxSplitGlAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        if (!TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId))
        {
            return 0;
        }

        await EnsureSalesTaxAccountHierarchyAsync(companyId, now, userName, cancellationToken);

        var totalTaxAccountId = await GetAccountIdAsync(companyId, SalesTaxPayable, cancellationToken);
        var salesTax18AccountId = await EnsureSalesTaxPayable18AccountAsync(
            companyId,
            now,
            userName,
            cancellationToken);
        var furtherTaxAccountId = await EnsureFurtherTaxPayableAccountAsync(
            companyId,
            now,
            userName,
            cancellationToken);
        if (!totalTaxAccountId.HasValue || !salesTax18AccountId.HasValue || !furtherTaxAccountId.HasValue)
        {
            return 0;
        }

        var taxRates = await _unitOfWork.Repository<TaxSetting>()
            .Query()
            .Where(t => t.CompanyId == companyId)
            .Select(t => new { t.SalesTaxRate, t.UnregisteredSalesTaxRate })
            .FirstOrDefaultAsync(cancellationToken);
        var registeredRate = taxRates?.SalesTaxRate ?? 18m;
        var unregisteredRate = taxRates?.UnregisteredSalesTaxRate ?? 22m;

        var scenarioCodes = await _unitOfWork.Repository<ScenarioType>()
            .Query()
            .ToDictionaryAsync(s => s.ScenarioId, s => s.Code, cancellationToken);

        var invoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query(asNoTracking: false)
            .Where(si =>
                si.CompanyId == companyId
                && si.Status == InvoiceStatus.Posted
                && si.JournalEntryId != null
                && si.TaxAmount + si.FurtherTax > 0m)
            .ToListAsync(cancellationToken);

        if (invoices.Count == 0)
        {
            return 0;
        }

        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var invoiceLineRows = await _unitOfWork.Repository<SalesInvoiceLine>()
            .Query()
            .Where(l => invoiceIds.Contains(l.SalesInvoiceId))
            .Select(l => new InvoiceLineTaxRow(
                l.SalesInvoiceId,
                l.Quantity,
                l.Price,
                l.Discount,
                l.TaxRate,
                l.Item.ItemType,
                l.Item.ItemCode))
            .ToListAsync(cancellationToken);

        var goodsLinesByInvoice = invoiceLineRows
            .Where(l => !SalesTaxSplit.IsCartageOrService(l.ItemType, l.ItemCode))
            .GroupBy(l => l.SalesInvoiceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var goodsTaxableByInvoice = goodsLinesByInvoice.ToDictionary(
            g => g.Key,
            g => SalesTaxSplit.ComputeGoodsTaxable(
                g.Value.Select(l => (l.Quantity, l.Price, l.Discount))));

        var customerIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var customerFurtherRates = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.FurtherTaxRate })
            .ToDictionaryAsync(c => c.Id, c => c.FurtherTaxRate, cancellationToken);

        var arAccountId = await GetAccountIdAsync(companyId, AccountsReceivable, cancellationToken);

        var adjusted = 0;

        foreach (var invoice in invoices)
        {
            if (!invoice.ScenarioId.HasValue
                || !scenarioCodes.TryGetValue(invoice.ScenarioId.Value, out var scenarioCode)
                || !SalesTaxSplit.IsUnregisteredScenario(scenarioCode))
            {
                continue;
            }

            if (!goodsTaxableByInvoice.TryGetValue(invoice.Id, out var goodsTaxable) || goodsTaxable <= 0m)
            {
                continue;
            }

            var journalId = invoice.JournalEntryId!.Value;
            customerFurtherRates.TryGetValue(invoice.CustomerId, out var customerFurtherTaxRate);
            goodsLinesByInvoice.TryGetValue(invoice.Id, out var goodsLines);
            var (salesTaxAmount, furtherTaxAmount) = ComputeSplitTaxFromLines(
                companyId,
                goodsTaxable,
                goodsLines ?? [],
                registeredRate,
                unregisteredRate,
                customerFurtherTaxRate);

            var newNetTotal = Math.Round(
                invoice.SubTotal - invoice.DiscountAmount + salesTaxAmount + furtherTaxAmount,
                2);

            var journalLines = await _unitOfWork.Repository<JournalEntryLine>()
                .Query(asNoTracking: false)
                .Where(l => l.JournalEntryId == journalId)
                .ToListAsync(cancellationToken);

            var isCreditNote = invoice.InvoiceType == InvoiceType.CreditNote;
            decimal LineAmount(JournalEntryLine line) => isCreditNote ? line.Debit : line.Credit;

            var existingSalesTax = journalLines
                .Where(l => l.ChartOfAccountId == salesTax18AccountId.Value)
                .Sum(LineAmount);
            var existingFurtherTax = journalLines
                .Where(l => l.ChartOfAccountId == furtherTaxAccountId.Value)
                .Sum(LineAmount);
            var existingTotalTaxAccount = journalLines
                .Where(l => l.ChartOfAccountId == totalTaxAccountId.Value)
                .Sum(LineAmount);

            var headerMatches = Math.Abs(invoice.TaxAmount - salesTaxAmount) < 0.01m
                && Math.Abs(invoice.FurtherTax - furtherTaxAmount) < 0.01m
                && Math.Abs(invoice.NetTotal - newNetTotal) < 0.01m;

            if (headerMatches
                && Math.Abs(existingSalesTax - salesTaxAmount) < 0.01m
                && Math.Abs(existingFurtherTax - furtherTaxAmount) < 0.01m
                && existingTotalTaxAccount <= 0.01m)
            {
                continue;
            }

            var taxAccountIds = new HashSet<int>
            {
                totalTaxAccountId.Value,
                salesTax18AccountId.Value,
                furtherTaxAccountId.Value
            };

            foreach (var line in journalLines.Where(l => taxAccountIds.Contains(l.ChartOfAccountId)))
            {
                _unitOfWork.Repository<JournalEntryLine>().Remove(line);
            }

            if (salesTaxAmount > 0m)
            {
                await _unitOfWork.Repository<JournalEntryLine>().AddAsync(new JournalEntryLine
                {
                    JournalEntryId = journalId,
                    ChartOfAccountId = salesTax18AccountId.Value,
                    Debit = isCreditNote ? salesTaxAmount : 0m,
                    Credit = isCreditNote ? 0m : salesTaxAmount,
                    Memo = "Sales Tax Payable (18%)"
                }, cancellationToken);
            }

            if (furtherTaxAmount > 0m)
            {
                await _unitOfWork.Repository<JournalEntryLine>().AddAsync(new JournalEntryLine
                {
                    JournalEntryId = journalId,
                    ChartOfAccountId = furtherTaxAccountId.Value,
                    Debit = isCreditNote ? furtherTaxAmount : 0m,
                    Credit = isCreditNote ? 0m : furtherTaxAmount,
                    Memo = "Further Tax Payable"
                }, cancellationToken);
            }

            if (arAccountId.HasValue)
            {
                foreach (var arLine in journalLines.Where(l => l.ChartOfAccountId == arAccountId.Value))
                {
                    arLine.Debit = isCreditNote ? 0m : newNetTotal;
                    arLine.Credit = isCreditNote ? newNetTotal : 0m;
                    _unitOfWork.Repository<JournalEntryLine>().Update(arLine);
                }
            }

            invoice.TaxAmount = salesTaxAmount;
            invoice.FurtherTax = furtherTaxAmount;
            invoice.NetTotal = newNetTotal;
            invoice.UpdatedAt = now;
            invoice.UpdatedBy = userName;
            _unitOfWork.Repository<SalesInvoice>().Update(invoice);

            var journal = await _unitOfWork.Repository<JournalEntry>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(j => j.Id == journalId && j.CompanyId == companyId, cancellationToken);

            if (journal is not null)
            {
                journal.UpdatedAt = now;
                journal.UpdatedBy = userName;
                _unitOfWork.Repository<JournalEntry>().Update(journal);
            }

            adjusted++;
        }

        return adjusted;
    }

    private async Task<int> FixInvertedSalesInvoiceArLinesAsync(
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

        var invoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query(asNoTracking: false)
            .Where(si =>
                si.CompanyId == companyId
                && si.Status == InvoiceStatus.Posted
                && si.JournalEntryId != null)
            .ToListAsync(cancellationToken);

        var fixedCount = 0;

        foreach (var invoice in invoices)
        {
            var journalId = invoice.JournalEntryId!.Value;
            var arLines = await _unitOfWork.Repository<JournalEntryLine>()
                .Query(asNoTracking: false)
                .Where(l => l.JournalEntryId == journalId && l.ChartOfAccountId == arAccountId.Value)
                .ToListAsync(cancellationToken);

            if (arLines.Count == 0)
            {
                continue;
            }

            var isCreditNote = invoice.InvoiceType == InvoiceType.CreditNote;
            var expectedAmount = Math.Round(invoice.NetTotal, 2);
            var invoiceChanged = false;

            foreach (var arLine in arLines)
            {
                var lineChanged = false;

                if (isCreditNote)
                {
                    if (arLine.Debit > 0m && arLine.Credit == 0m)
                    {
                        arLine.Credit = Math.Round(arLine.Debit, 2);
                        arLine.Debit = 0m;
                        lineChanged = true;
                    }
                    else if (arLine.Credit != expectedAmount && arLine.Debit == 0m)
                    {
                        arLine.Credit = expectedAmount;
                        lineChanged = true;
                    }
                }
                else if (arLine.Credit > 0m && arLine.Debit == 0m)
                {
                    arLine.Debit = expectedAmount;
                    arLine.Credit = 0m;
                    lineChanged = true;
                }
                else if (arLine.Debit != expectedAmount && arLine.Credit == 0m)
                {
                    arLine.Debit = expectedAmount;
                    lineChanged = true;
                }

                if (lineChanged)
                {
                    _unitOfWork.Repository<JournalEntryLine>().Update(arLine);
                    invoiceChanged = true;
                }
            }

            if (!invoiceChanged)
            {
                continue;
            }

            var journal = await _unitOfWork.Repository<JournalEntry>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(j => j.Id == journalId && j.CompanyId == companyId, cancellationToken);

            if (journal is not null)
            {
                journal.UpdatedAt = now;
                journal.UpdatedBy = userName;
                _unitOfWork.Repository<JournalEntry>().Update(journal);
            }

            fixedCount++;
        }

        return fixedCount;
    }

    private sealed record InvoiceLineTaxRow(
        int SalesInvoiceId,
        decimal Quantity,
        decimal Price,
        decimal Discount,
        decimal TaxRate,
        ItemType ItemType,
        string? ItemCode);

    private static (decimal SalesTax, decimal FurtherTax) ComputeSplitTaxFromLines(
        int companyId,
        decimal goodsTaxable,
        IReadOnlyList<InvoiceLineTaxRow> goodsLines,
        decimal registeredRate,
        decimal unregisteredRate,
        decimal? customerFurtherRate)
    {
        var defaultFurtherRate = SalesTaxSplit.FurtherTaxRate(registeredRate, unregisteredRate);
        var furtherRateOverride = customerFurtherRate ?? defaultFurtherRate;

        if (TradeInvoiceLayout.UsesUnregisteredBillLevelTaxSplit(companyId))
        {
            var salesTax = Math.Round(goodsTaxable * registeredRate / 100m, 2);
            if (!goodsLines.Any(l => SalesTaxSplit.AppliesFurtherTax(l.TaxRate, registeredRate)))
            {
                return (salesTax, 0m);
            }

            decimal furtherTax = 0m;
            foreach (var line in goodsLines)
            {
                if (!SalesTaxSplit.AppliesFurtherTax(line.TaxRate, registeredRate))
                {
                    continue;
                }

                var taxable = SalesTaxSplit.ComputeLineTaxable(line.Quantity, line.Price, line.Discount);
                furtherTax += Math.Round(taxable * furtherRateOverride / 100m, 2);
            }

            return (salesTax, Math.Round(furtherTax, 2));
        }

        decimal salesTaxTotal = 0m;
        decimal furtherTaxTotal = 0m;
        foreach (var line in goodsLines)
        {
            var taxable = SalesTaxSplit.ComputeLineTaxable(line.Quantity, line.Price, line.Discount);
            var applyFurther = SalesTaxSplit.ApplyFurtherTaxForLine(true, line.TaxRate, registeredRate);
            var (salesTax, furtherTax, _) = SalesTaxSplit.CalculateLineTax(
                taxable,
                registeredRate,
                unregisteredRate,
                applyFurther,
                furtherRateOverride,
                line.TaxRate);
            salesTaxTotal += salesTax;
            furtherTaxTotal += furtherTax;
        }

        return (Math.Round(salesTaxTotal, 2), Math.Round(furtherTaxTotal, 2));
    }

    private async Task EnsureSalesTaxAccountHierarchyAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        if (!TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId))
        {
            return;
        }

        var parent = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AccountNumber == SalesTaxPayable, cancellationToken);

        if (parent is null)
        {
            return;
        }

        var childNumbers = new[] { SalesTaxPayable18, FurtherTaxPayable };
        foreach (var childNumber in childNumbers)
        {
            var child = await _unitOfWork.Repository<ChartOfAccount>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AccountNumber == childNumber, cancellationToken);

            if (child is null)
            {
                continue;
            }

            if (child.ParentAccountId != parent.Id)
            {
                child.ParentAccountId = parent.Id;
                child.UpdatedAt = now;
                child.UpdatedBy = userName;
                _unitOfWork.Repository<ChartOfAccount>().Update(child);
            }
        }

        await ReallocateSalesTaxOpeningBalanceCoreAsync(companyId, now, userName, cancellationToken);
    }

    private async Task<decimal> ReallocateSalesTaxOpeningBalanceCoreAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        if (!TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId))
        {
            return 0m;
        }

        var parent = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(
                a => a.CompanyId == companyId && a.AccountNumber == SalesTaxPayable && !a.IsDeleted,
                cancellationToken);

        var salesTax18 = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(
                a => a.CompanyId == companyId && a.AccountNumber == SalesTaxPayable18 && !a.IsDeleted,
                cancellationToken);

        if (parent is null || salesTax18 is null || parent.OpeningBalance == 0m)
        {
            return 0m;
        }

        var amountToMove = parent.OpeningBalance;
        salesTax18.OpeningBalance = Math.Round(salesTax18.OpeningBalance + amountToMove, 2);
        parent.OpeningBalance = 0m;

        salesTax18.UpdatedAt = now;
        salesTax18.UpdatedBy = userName;
        parent.UpdatedAt = now;
        parent.UpdatedBy = userName;

        _unitOfWork.Repository<ChartOfAccount>().Update(salesTax18);
        _unitOfWork.Repository<ChartOfAccount>().Update(parent);

        return amountToMove;
    }

    private async Task<int?> EnsureSalesTaxPayable18AccountAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var existing = await GetAccountIdAsync(companyId, SalesTaxPayable18, cancellationToken);
        if (existing.HasValue)
        {
            return existing;
        }

        var salesTax = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.AccountNumber == SalesTaxPayable)
            .Select(a => new { a.TypeId, a.SubTypeId })
            .FirstOrDefaultAsync(cancellationToken);

        var account = new ChartOfAccount
        {
            CompanyId = companyId,
            AccountNumber = SalesTaxPayable18,
            AccountName = "Sales Tax Payable (18%)",
            TypeId = salesTax?.TypeId ?? 2,
            SubTypeId = salesTax?.SubTypeId ?? 10,
            IsActive = true,
            OpeningBalance = 0m,
            CreatedAt = now,
            CreatedBy = userName
        };

        await _unitOfWork.Repository<ChartOfAccount>().AddAsync(account, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return account.Id;
    }

    private async Task<int?> EnsureFurtherTaxPayableAccountAsync(
        int companyId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var existing = await GetAccountIdAsync(companyId, FurtherTaxPayable, cancellationToken);
        if (existing.HasValue)
        {
            return existing;
        }

        var salesTax = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.AccountNumber == SalesTaxPayable)
            .Select(a => new { a.TypeId, a.SubTypeId })
            .FirstOrDefaultAsync(cancellationToken);

        var account = new ChartOfAccount
        {
            CompanyId = companyId,
            AccountNumber = FurtherTaxPayable,
            AccountName = "Further Tax Payable",
            TypeId = salesTax?.TypeId ?? 2,
            SubTypeId = salesTax?.SubTypeId ?? 10,
            IsActive = true,
            OpeningBalance = 0m,
            CreatedAt = now,
            CreatedBy = userName
        };

        await _unitOfWork.Repository<ChartOfAccount>().AddAsync(account, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return account.Id;
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

        var invoiceJournals = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Where(j =>
                j.CompanyId == companyId
                && !j.IsDeleted
                && j.ReferenceType == ReferenceTypes.SalesInvoice
                && j.Status == JournalStatus.Posted)
            .ToListAsync(cancellationToken);

        var duplicateInvoiceJournals = invoiceJournals
            .GroupBy(j => j.ReferenceId)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);

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
            .Select(a => new { a.Id, a.OpeningBalance, a.TypeId, a.AccountNumber })
            .ToListAsync(cancellationToken);

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

        var journalTotals = await journalQuery
            .GroupBy(l => l.ChartOfAccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit)
            })
            .ToListAsync(cancellationToken);

        var journalByAccount = journalTotals.ToDictionary(x => x.AccountId);

        decimal debits = 0m;
        decimal credits = 0m;

        foreach (var account in accounts)
        {
            journalByAccount.TryGetValue(account.Id, out var journal);
            var closingNet = GlAccountBalance.ComputeNet(
                account.OpeningBalance,
                journal?.Debit ?? 0m,
                journal?.Credit ?? 0m,
                account.TypeId,
                account.AccountNumber);
            var (closingDebit, closingCredit) = GlTrialBalanceColumns.SplitClosingBalance(
                closingNet,
                account.TypeId,
                account.AccountNumber);
            debits += closingDebit;
            credits += closingCredit;
        }

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

    private async Task<decimal> GetPostedJournalMovementAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        return Math.Round(
            await _unitOfWork.Repository<JournalEntryLine>()
                .Query()
                .Where(l =>
                    l.ChartOfAccountId == chartOfAccountId
                    && l.JournalEntry.CompanyId == companyId
                    && l.JournalEntry.Status == JournalStatus.Posted
                    && !l.JournalEntry.IsDeleted)
                .Select(l => l.Debit - l.Credit)
                .SumAsync(cancellationToken),
            2);
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

        var account = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.Id == accountId.Value && a.CompanyId == companyId)
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return 0m;
        }

        var totals = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l =>
                l.ChartOfAccountId == account.Id
                && l.JournalEntry.CompanyId == companyId
                && l.JournalEntry.Status == JournalStatus.Posted
                && !l.JournalEntry.IsDeleted)
            .GroupBy(_ => 1)
            .Select(g => new { Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .FirstOrDefaultAsync(cancellationToken);

        return Math.Round(
            GlAccountBalance.ComputeNet(
                account.OpeningBalance,
                totals?.Debit ?? 0m,
                totals?.Credit ?? 0m,
                account.TypeId,
                account.AccountNumber),
            2);
    }

    private const decimal InventoryAssetOpeningFromQuickBooks = 7_497_916.51m;

    public async Task<InventoryAssetRepairResult> RepairInventoryAssetFromQuickBooksAsync(
        int companyId,
        string quickBooksLedgerFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(quickBooksLedgerFilePath))
        {
            return new InventoryAssetRepairResult(
                false,
                $"QuickBooks ledger file not found: {quickBooksLedgerFilePath}",
                0,
                0,
                0m,
                0m,
                0m);
        }

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "inventory-qb-repair";
        var quickBooks = QuickBooksInventoryLedgerReader.Read(quickBooksLedgerFilePath);

        var inventoryAccountId = await GetAccountIdAsync(companyId, InventoryAsset, cancellationToken);
        var payableAccountId = await GetAccountIdAsync(companyId, AccountsPayable, cancellationToken);
        var cogsAccountId = await GetAccountIdAsync(companyId, CostOfGoodsSold, cancellationToken);
        var obeAccountId = await GetAccountIdAsync(companyId, OpeningBalanceEquity, cancellationToken);

        if (!inventoryAccountId.HasValue || !payableAccountId.HasValue || !cogsAccountId.HasValue || !obeAccountId.HasValue)
        {
            return new InventoryAssetRepairResult(
                false,
                "Inventory, AP, COGS, or Opening Balance Equity account not found.",
                0,
                0,
                quickBooks.ClosingBalance,
                0m,
                quickBooks.ClosingBalance);
        }

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            await RestoreInventoryOpeningBalanceAsync(
                companyId,
                InventoryAssetOpeningFromQuickBooks,
                obeAccountId.Value,
                now,
                userName,
                cancellationToken);

            var vendorBills = await _unitOfWork.Repository<VendorBill>()
                .Query(asNoTracking: false)
                .Where(b => b.CompanyId == companyId && b.JournalEntryId != null)
                .OrderBy(b => b.BillDate)
                .ThenBy(b => b.BillNumber)
                .ToListAsync(cancellationToken);

            var salesInvoices = await _unitOfWork.Repository<SalesInvoice>()
                .Query(asNoTracking: false)
                .Where(i =>
                    i.CompanyId == companyId
                    && i.Status == InvoiceStatus.Posted
                    && i.JournalEntryId != null
                    && i.InvoiceType != InvoiceType.DebitNote)
                .OrderBy(i => i.InvoiceDate)
                .ThenBy(i => i.InvoiceNumber)
                .ToListAsync(cancellationToken);

            if (vendorBills.Count != quickBooks.Bills.Count)
            {
                throw new InvalidOperationException(
                    $"Vendor bill count mismatch. ERP={vendorBills.Count}, QuickBooks={quickBooks.Bills.Count}.");
            }

            if (salesInvoices.Count != quickBooks.Invoices.Count)
            {
                throw new InvalidOperationException(
                    $"Sales invoice count mismatch. ERP={salesInvoices.Count}, QuickBooks={quickBooks.Invoices.Count}.");
            }

            var billsUpdated = 0;
            for (var index = 0; index < vendorBills.Count; index++)
            {
                var bill = vendorBills[index];
                var target = quickBooks.Bills[index];
                if (!bill.JournalEntryId.HasValue)
                {
                    continue;
                }

                var changed = await ApplyQuickBooksVendorBillInventoryAsync(
                    bill,
                    bill.JournalEntryId.Value,
                    inventoryAccountId.Value,
                    payableAccountId.Value,
                    target,
                    now,
                    userName,
                    cancellationToken);

                if (changed)
                {
                    billsUpdated++;
                }
            }

            var invoicesUpdated = 0;
            for (var index = 0; index < salesInvoices.Count; index++)
            {
                var invoice = salesInvoices[index];
                var target = quickBooks.Invoices[index];
                if (!invoice.JournalEntryId.HasValue)
                {
                    continue;
                }

                var changed = await ApplyQuickBooksSalesInvoiceCogsAsync(
                    invoice.JournalEntryId.Value,
                    inventoryAccountId.Value,
                    cogsAccountId.Value,
                    target.CogsCredit,
                    now,
                    userName,
                    cancellationToken);

                if (changed)
                {
                    invoicesUpdated++;
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var erpClosing = await GetAccountBalanceAsync(companyId, InventoryAsset, cancellationToken);

            return new InventoryAssetRepairResult(
                true,
                $"Inventory asset repaired from QuickBooks ledger. Bills updated: {billsUpdated}, invoices updated: {invoicesUpdated}.",
                billsUpdated,
                invoicesUpdated,
                quickBooks.ClosingBalance,
                erpClosing,
                Math.Round(erpClosing - quickBooks.ClosingBalance, 2));
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Inventory asset repair failed for company {CompanyId}", companyId);
            return new InventoryAssetRepairResult(false, ex.Message, 0, 0, quickBooks.ClosingBalance, 0m, 0m);
        }
    }

    public async Task<QuickBooksControlBalanceAlignResult> AlignControlAccountsToQuickBooksAsync(
        int companyId,
        decimal accountsReceivableBalance,
        decimal accountsPayableBalance,
        decimal inventoryBalance,
        decimal? furtherTaxBalance = null,
        decimal? salesTax18Balance = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "qb-control-align";
        var obeAccountId = await GetAccountIdAsync(companyId, OpeningBalanceEquity, cancellationToken);
        if (!obeAccountId.HasValue)
        {
            return new QuickBooksControlBalanceAlignResult(
                false,
                "Opening Balance Equity account not found.",
                0m,
                0m,
                0m,
                null,
                null,
                0m,
                0m,
                0m);
        }

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            await AlignAccountOpeningToClosingTargetAsync(
                companyId,
                AccountsReceivable,
                accountsReceivableBalance,
                now,
                userName,
                cancellationToken);
            await AlignAccountOpeningToClosingTargetAsync(
                companyId,
                AccountsPayable,
                accountsPayableBalance,
                now,
                userName,
                cancellationToken);
            await AlignAccountOpeningToClosingTargetAsync(
                companyId,
                InventoryAsset,
                inventoryBalance,
                now,
                userName,
                cancellationToken);

            if (furtherTaxBalance.HasValue)
            {
                await EnsureSalesTaxAccountHierarchyAsync(companyId, now, userName, cancellationToken);
                var parent = await _unitOfWork.Repository<ChartOfAccount>()
                    .Query(asNoTracking: false)
                    .FirstAsync(a => a.CompanyId == companyId && a.AccountNumber == SalesTaxPayable, cancellationToken);
                parent.OpeningBalance = 0m;
                parent.UpdatedAt = now;
                parent.UpdatedBy = userName;
                _unitOfWork.Repository<ChartOfAccount>().Update(parent);

                await AlignAccountOpeningToClosingTargetAsync(
                    companyId,
                    FurtherTaxPayable,
                    furtherTaxBalance.Value,
                    now,
                    userName,
                    cancellationToken);
            }

            if (salesTax18Balance.HasValue)
            {
                await EnsureSalesTaxAccountHierarchyAsync(companyId, now, userName, cancellationToken);
                await AlignAccountOpeningToClosingTargetAsync(
                    companyId,
                    SalesTaxPayable18,
                    salesTax18Balance.Value,
                    now,
                    userName,
                    cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await ReplugOpeningBalanceEquityAsync(companyId, obeAccountId.Value, now, userName, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var (trialDebits, trialCredits) = await GetTrialBalanceTotalsAsync(companyId, cancellationToken);
            var obe = await GetAccountBalanceAsync(companyId, OpeningBalanceEquity, cancellationToken);

            return new QuickBooksControlBalanceAlignResult(
                true,
                "Control account openings aligned to QuickBooks closing balances.",
                await GetAccountBalanceAsync(companyId, AccountsReceivable, cancellationToken),
                await GetAccountBalanceAsync(companyId, AccountsPayable, cancellationToken),
                await GetAccountBalanceAsync(companyId, InventoryAsset, cancellationToken),
                furtherTaxBalance.HasValue
                    ? await GetAccountBalanceAsync(companyId, FurtherTaxPayable, cancellationToken)
                    : null,
                salesTax18Balance.HasValue
                    ? await GetAccountBalanceAsync(companyId, SalesTaxPayable18, cancellationToken)
                    : null,
                obe,
                trialDebits,
                trialCredits);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "QuickBooks control balance alignment failed for company {CompanyId}", companyId);
            return new QuickBooksControlBalanceAlignResult(
                false,
                ex.Message,
                0m,
                0m,
                0m,
                null,
                null,
                0m,
                0m,
                0m);
        }
    }

    public async Task<QuickBooksControlBalanceAlignResult> AlignSalesTaxFromQuickBooksAsync(
        int companyId,
        string salesTaxPayableFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(salesTaxPayableFilePath))
        {
            return new QuickBooksControlBalanceAlignResult(
                false,
                $"Sales tax payable file not found: {salesTaxPayableFilePath}",
                0m,
                0m,
                0m,
                null,
                null,
                0m,
                0m,
                0m);
        }

        var quickBooks = QuickBooksSalesTaxPayableReader.Read(salesTaxPayableFilePath);
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "sales-tax-qb-align";
        var obeAccountId = await GetAccountIdAsync(companyId, OpeningBalanceEquity, cancellationToken);

        if (!obeAccountId.HasValue)
        {
            return new QuickBooksControlBalanceAlignResult(
                false,
                "Opening Balance Equity account not found.",
                0m,
                0m,
                0m,
                null,
                null,
                0m,
                0m,
                0m);
        }

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            await EnsureSalesTaxAccountHierarchyAsync(companyId, now, userName, cancellationToken);

            var parent = await _unitOfWork.Repository<ChartOfAccount>()
                .Query(asNoTracking: false)
                .FirstAsync(a => a.CompanyId == companyId && a.AccountNumber == SalesTaxPayable, cancellationToken);
            parent.OpeningBalance = 0m;
            parent.UpdatedAt = now;
            parent.UpdatedBy = userName;
            _unitOfWork.Repository<ChartOfAccount>().Update(parent);

            await AlignAccountOpeningToClosingTargetAsync(
                companyId,
                FurtherTaxPayable,
                quickBooks.FurtherTaxClosingBalance,
                now,
                userName,
                cancellationToken);
            await AlignAccountOpeningToClosingTargetAsync(
                companyId,
                SalesTaxPayable18,
                quickBooks.SalesTax18ClosingBalance,
                now,
                userName,
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await ReplugOpeningBalanceEquityAsync(companyId, obeAccountId.Value, now, userName, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var (trialDebits, trialCredits) = await GetTrialBalanceTotalsAsync(companyId, cancellationToken);
            var obe = await GetAccountBalanceAsync(companyId, OpeningBalanceEquity, cancellationToken);
            var further = await GetAccountBalanceAsync(companyId, FurtherTaxPayable, cancellationToken);
            var salesTax18 = await GetAccountBalanceAsync(companyId, SalesTaxPayable18, cancellationToken);

            return new QuickBooksControlBalanceAlignResult(
                true,
                $"Sales tax aligned to QuickBooks closing {quickBooks.ClosingBalance:N2} " +
                $"(4%/2% {quickBooks.FurtherTaxClosingBalance:N2}, 18% {quickBooks.SalesTax18ClosingBalance:N2}).",
                await GetAccountBalanceAsync(companyId, AccountsReceivable, cancellationToken),
                await GetAccountBalanceAsync(companyId, AccountsPayable, cancellationToken),
                await GetAccountBalanceAsync(companyId, InventoryAsset, cancellationToken),
                further,
                salesTax18,
                obe,
                trialDebits,
                trialCredits);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Sales tax QuickBooks alignment failed for company {CompanyId}", companyId);
            return new QuickBooksControlBalanceAlignResult(
                false,
                ex.Message,
                0m,
                0m,
                0m,
                null,
                null,
                0m,
                0m,
                0m);
        }
    }

    private async Task AlignAccountOpeningToClosingTargetAsync(
        int companyId,
        string accountNumber,
        decimal targetClosingBalance,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstAsync(a => a.CompanyId == companyId && a.AccountNumber == accountNumber, cancellationToken);

        var journalTotals = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l =>
                l.ChartOfAccountId == account.Id
                && l.JournalEntry.CompanyId == companyId
                && l.JournalEntry.Status == JournalStatus.Posted
                && !l.JournalEntry.IsDeleted)
            .GroupBy(_ => 1)
            .Select(g => new { Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .FirstOrDefaultAsync(cancellationToken);

        var journalDelta = GlAccountBalance.GetJournalDelta(
            journalTotals?.Debit ?? 0m,
            journalTotals?.Credit ?? 0m,
            account.TypeId,
            account.AccountNumber);

        account.OpeningBalance = Math.Round(targetClosingBalance - journalDelta, 2);
        account.UpdatedAt = now;
        account.UpdatedBy = userName;
        _unitOfWork.Repository<ChartOfAccount>().Update(account);
    }

    public async Task<InventoryAssetAlignResult> AlignInventoryAssetToQuickBooksAsync(
        int companyId,
        decimal quickBooksClosingBalance,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "inventory-qb-align";
        var obeAccountId = await GetAccountIdAsync(companyId, OpeningBalanceEquity, cancellationToken);
        if (!obeAccountId.HasValue)
        {
            return new InventoryAssetAlignResult(
                false,
                "Opening Balance Equity account not found.",
                quickBooksClosingBalance,
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

            var inventoryAccount = await _unitOfWork.Repository<ChartOfAccount>()
                .Query(asNoTracking: false)
                .FirstAsync(a => a.CompanyId == companyId && a.AccountNumber == InventoryAsset, cancellationToken);

            var oldOpening = inventoryAccount.OpeningBalance;
            var journalNet = await _unitOfWork.Repository<JournalEntryLine>()
                .Query()
                .Where(l =>
                    l.ChartOfAccountId == inventoryAccount.Id
                    && l.JournalEntry.CompanyId == companyId
                    && l.JournalEntry.Status == JournalStatus.Posted
                    && !l.JournalEntry.IsDeleted)
                .Select(l => l.Debit - l.Credit)
                .SumAsync(cancellationToken);

            var newOpening = Math.Round(quickBooksClosingBalance - journalNet, 2);
            inventoryAccount.OpeningBalance = newOpening;
            inventoryAccount.UpdatedAt = now;
            inventoryAccount.UpdatedBy = userName;
            _unitOfWork.Repository<ChartOfAccount>().Update(inventoryAccount);

            await ReplugOpeningBalanceEquityAsync(companyId, obeAccountId.Value, now, userName, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var erpClosing = await GetAccountBalanceAsync(companyId, InventoryAsset, cancellationToken);
            var itemValuation = await _unitOfWork.Repository<Item>()
                .Query()
                .Where(i => i.CompanyId == companyId && i.IsActive)
                .Select(i => i.CurrentStock * i.PurchaseRate)
                .SumAsync(cancellationToken);

            return new InventoryAssetAlignResult(
                true,
                "Inventory asset opening balance aligned to QuickBooks closing.",
                quickBooksClosingBalance,
                oldOpening,
                newOpening,
                journalNet,
                erpClosing,
                Math.Round(itemValuation, 2),
                Math.Round(erpClosing - quickBooksClosingBalance, 2));
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Inventory asset alignment failed for company {CompanyId}", companyId);
            return new InventoryAssetAlignResult(
                false,
                ex.Message,
                quickBooksClosingBalance,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m);
        }
    }

    public async Task<InventoryAssetAlignResult> AlignInventoryAssetToStockSummaryAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "inventory-stock-align";
        var obeAccountId = await GetAccountIdAsync(companyId, OpeningBalanceEquity, cancellationToken);
        if (!obeAccountId.HasValue)
        {
            return new InventoryAssetAlignResult(
                false,
                "Opening Balance Equity account not found.",
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

            var inventoryAccount = await _unitOfWork.Repository<ChartOfAccount>()
                .Query(asNoTracking: false)
                .FirstAsync(a => a.CompanyId == companyId && a.AccountNumber == InventoryAsset, cancellationToken);

            var stockSummaryValue = await _unitOfWork.Repository<Item>()
                .Query()
                .Where(i => i.CompanyId == companyId)
                .Select(i => i.CurrentStock * i.PurchaseRate)
                .SumAsync(cancellationToken);

            stockSummaryValue = Math.Round(stockSummaryValue, 2);

            var journalNet = await _unitOfWork.Repository<JournalEntryLine>()
                .Query()
                .Where(l =>
                    l.ChartOfAccountId == inventoryAccount.Id
                    && l.JournalEntry.CompanyId == companyId
                    && l.JournalEntry.Status == JournalStatus.Posted
                    && !l.JournalEntry.IsDeleted)
                .Select(l => l.Debit - l.Credit)
                .SumAsync(cancellationToken);

            var oldOpening = inventoryAccount.OpeningBalance;
            var newOpening = Math.Round(stockSummaryValue - journalNet, 2);
            inventoryAccount.OpeningBalance = newOpening;
            inventoryAccount.UpdatedAt = now;
            inventoryAccount.UpdatedBy = userName;
            _unitOfWork.Repository<ChartOfAccount>().Update(inventoryAccount);

            await ReplugOpeningBalanceEquityAsync(companyId, obeAccountId.Value, now, userName, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var erpClosing = await GetAccountBalanceAsync(companyId, InventoryAsset, cancellationToken);

            return new InventoryAssetAlignResult(
                true,
                Math.Abs(erpClosing - stockSummaryValue) < 0.02m
                    ? "Inventory asset balance now matches stock summary valuation."
                    : $"Inventory asset adjusted toward stock summary. Remaining difference: {Math.Round(erpClosing - stockSummaryValue, 2):N2}.",
                stockSummaryValue,
                oldOpening,
                newOpening,
                journalNet,
                erpClosing,
                stockSummaryValue,
                Math.Round(erpClosing - stockSummaryValue, 2));
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Inventory asset stock summary alignment failed for company {CompanyId}", companyId);
            return new InventoryAssetAlignResult(
                false,
                ex.Message,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m);
        }
    }

    private async Task RestoreInventoryOpeningBalanceAsync(
        int companyId,
        decimal openingBalance,
        int obeAccountId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var inventoryAccount = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstAsync(a => a.CompanyId == companyId && a.AccountNumber == InventoryAsset, cancellationToken);

        inventoryAccount.OpeningBalance = Math.Round(openingBalance, 2);
        inventoryAccount.UpdatedAt = now;
        inventoryAccount.UpdatedBy = userName;
        _unitOfWork.Repository<ChartOfAccount>().Update(inventoryAccount);

        await ReplugOpeningBalanceEquityAsync(companyId, obeAccountId, now, userName, cancellationToken);
    }

    private async Task ReplugOpeningBalanceEquityAsync(
        int companyId,
        int obeAccountId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var otherAccounts = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.Id != obeAccountId)
            .Select(a => new { a.Id, a.OpeningBalance, a.TypeId, a.AccountNumber })
            .ToListAsync(cancellationToken);

        var journalTotals = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l =>
                l.JournalEntry.CompanyId == companyId
                && l.JournalEntry.Status == JournalStatus.Posted
                && !l.JournalEntry.IsDeleted)
            .GroupBy(l => l.ChartOfAccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit)
            })
            .ToListAsync(cancellationToken);

        var journalByAccount = journalTotals.ToDictionary(x => x.AccountId);

        decimal closingDebits = 0m;
        decimal closingCredits = 0m;
        foreach (var account in otherAccounts)
        {
            journalByAccount.TryGetValue(account.Id, out var journal);
            var closingNet = GlAccountBalance.ComputeNet(
                account.OpeningBalance,
                journal?.Debit ?? 0m,
                journal?.Credit ?? 0m,
                account.TypeId,
                account.AccountNumber);
            var (debit, credit) = GlTrialBalanceColumns.SplitClosingBalance(
                closingNet,
                account.TypeId,
                account.AccountNumber);
            closingDebits += debit;
            closingCredits += credit;
        }

        var plug = Math.Round(closingCredits - closingDebits, 2);

        var obeAccount = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstAsync(a => a.Id == obeAccountId, cancellationToken);

        obeAccount.OpeningBalance = Math.Round(plug, 2);
        obeAccount.UpdatedAt = now;
        obeAccount.UpdatedBy = userName;
        _unitOfWork.Repository<ChartOfAccount>().Update(obeAccount);
    }

    private async Task<bool> ApplyQuickBooksVendorBillInventoryAsync(
        VendorBill bill,
        int journalEntryId,
        int inventoryAccountId,
        int payableAccountId,
        QuickBooksInventoryBillAmounts target,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var journalLines = await _unitOfWork.Repository<JournalEntryLine>()
            .Query(asNoTracking: false)
            .Where(l => l.JournalEntryId == journalEntryId)
            .ToListAsync(cancellationToken);

        var inventoryLines = journalLines
            .Where(l => l.ChartOfAccountId == inventoryAccountId)
            .ToList();
        var payableLine = journalLines.FirstOrDefault(l => l.ChartOfAccountId == payableAccountId);

        if (payableLine is null)
        {
            throw new InvalidOperationException(
                $"Accounts Payable line not found for vendor bill {bill.BillNumber}.");
        }

        var oldInventoryNet = Math.Round(
            inventoryLines.Sum(l => l.Debit) - inventoryLines.Sum(l => l.Credit),
            2);
        var targetInventoryNet = target.InventoryNet;
        if (oldInventoryNet == targetInventoryNet
            && inventoryLines.Count == (target.InventoryDebit > 0m ? 1 : 0) + (target.InventoryCredit > 0m ? 1 : 0)
            && inventoryLines.Sum(l => l.Debit) == target.InventoryDebit
            && inventoryLines.Sum(l => l.Credit) == target.InventoryCredit)
        {
            return false;
        }

        foreach (var line in inventoryLines)
        {
            _unitOfWork.Repository<JournalEntryLine>().Remove(line);
        }

        if (target.InventoryDebit > 0m)
        {
            await _unitOfWork.Repository<JournalEntryLine>().AddAsync(new JournalEntryLine
            {
                JournalEntryId = journalEntryId,
                ChartOfAccountId = inventoryAccountId,
                Debit = target.InventoryDebit,
                Credit = 0m,
                Memo = "Inventory Asset"
            }, cancellationToken);
        }

        if (target.InventoryCredit > 0m)
        {
            await _unitOfWork.Repository<JournalEntryLine>().AddAsync(new JournalEntryLine
            {
                JournalEntryId = journalEntryId,
                ChartOfAccountId = inventoryAccountId,
                Debit = 0m,
                Credit = target.InventoryCredit,
                Memo = "Inventory Asset"
            }, cancellationToken);
        }

        var delta = Math.Round(targetInventoryNet - oldInventoryNet, 2);
        payableLine.Credit = Math.Round(payableLine.Credit + delta, 2);
        _unitOfWork.Repository<JournalEntryLine>().Update(payableLine);

        bill.NetAmount = Math.Round(bill.NetAmount + delta, 2);
        bill.UpdatedAt = now;
        bill.UpdatedBy = userName;
        _unitOfWork.Repository<VendorBill>().Update(bill);

        var journal = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .FirstAsync(j => j.Id == journalEntryId, cancellationToken);
        journal.UpdatedAt = now;
        journal.UpdatedBy = userName;
        _unitOfWork.Repository<JournalEntry>().Update(journal);

        return true;
    }

    private async Task<bool> ApplyQuickBooksSalesInvoiceCogsAsync(
        int journalEntryId,
        int inventoryAccountId,
        int cogsAccountId,
        decimal targetCogs,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var journalLines = await _unitOfWork.Repository<JournalEntryLine>()
            .Query(asNoTracking: false)
            .Where(l => l.JournalEntryId == journalEntryId)
            .ToListAsync(cancellationToken);

        var cogsLine = journalLines.FirstOrDefault(l => l.ChartOfAccountId == cogsAccountId);
        var inventoryLine = journalLines.FirstOrDefault(l => l.ChartOfAccountId == inventoryAccountId);

        if (cogsLine is null || inventoryLine is null)
        {
            return false;
        }

        var currentCogs = Math.Round(
            cogsLine.Debit > 0m ? cogsLine.Debit : cogsLine.Credit,
            2);

        if (currentCogs == targetCogs)
        {
            return false;
        }

        if (cogsLine.Debit > 0m)
        {
            cogsLine.Debit = targetCogs;
            inventoryLine.Credit = targetCogs;
        }
        else
        {
            cogsLine.Credit = targetCogs;
            inventoryLine.Debit = targetCogs;
        }

        _unitOfWork.Repository<JournalEntryLine>().Update(cogsLine);
        _unitOfWork.Repository<JournalEntryLine>().Update(inventoryLine);

        var journal = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .FirstAsync(j => j.Id == journalEntryId, cancellationToken);
        journal.UpdatedAt = now;
        journal.UpdatedBy = userName;
        _unitOfWork.Repository<JournalEntry>().Update(journal);

        return true;
    }

    public async Task<VendorBillApRepairResult> RepairVendorBillsFromQuickBooksApAsync(
        int companyId,
        string accountsPayableFilePath,
        bool applyFixes = true,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(accountsPayableFilePath))
        {
            return new VendorBillApRepairResult(
                false,
                $"Accounts Payable file not found: {accountsPayableFilePath}",
                0,
                0,
                0,
                0,
                0m,
                0m,
                0m,
                Array.Empty<VendorBillApMismatchDto>());
        }

        var quickBooks = QuickBooksAccountsPayableReader.Read(accountsPayableFilePath);
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "ap-qb-repair";

        var inventoryAccountId = await GetAccountIdAsync(companyId, InventoryAsset, cancellationToken);
        var payableAccountId = await GetAccountIdAsync(companyId, AccountsPayable, cancellationToken);
        var inputTaxAccountId = await GetAccountIdAsync(companyId, PrepaidSalesTax, cancellationToken);

        if (!inventoryAccountId.HasValue || !payableAccountId.HasValue || !inputTaxAccountId.HasValue)
        {
            return new VendorBillApRepairResult(
                false,
                "Inventory, Accounts Payable, or Input Tax account not found.",
                0,
                0,
                0,
                0,
                quickBooks.ClosingBalance ?? 0m,
                0m,
                0m,
                Array.Empty<VendorBillApMismatchDto>());
        }

        var erpBills = await _unitOfWork.Repository<VendorBill>()
            .Query(asNoTracking: false)
            .Include(b => b.Vendor)
            .Where(b => b.CompanyId == companyId
                        && b.Status == BillStatus.Approved
                        && !string.IsNullOrWhiteSpace(b.RefNo))
            .ToListAsync(cancellationToken);

        var erpByRef = erpBills
            .GroupBy(b => b.RefNo!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var mismatches = new List<VendorBillApMismatchDto>();
        var billsUpdated = 0;
        var missingInErp = 0;
        var fixCandidates = new List<(VendorBill Bill, decimal TargetNet)>();

        foreach (var qbBill in quickBooks.Bills)
        {
            if (!erpByRef.TryGetValue(qbBill.RefNo, out var erpBill))
            {
                missingInErp++;
                mismatches.Add(new VendorBillApMismatchDto(
                    qbBill.RefNo,
                    null,
                    qbBill.VendorName,
                    qbBill.NetAmount,
                    0m,
                    qbBill.NetAmount));
                continue;
            }

            var difference = Math.Round(qbBill.NetAmount - erpBill.NetAmount, 2);
            if (difference == 0m)
            {
                continue;
            }

            mismatches.Add(new VendorBillApMismatchDto(
                qbBill.RefNo,
                erpBill.BillNumber,
                erpBill.Vendor.VendorName,
                qbBill.NetAmount,
                erpBill.NetAmount,
                difference));

            if (applyFixes)
            {
                fixCandidates.Add((erpBill, qbBill.NetAmount));
            }
        }

        if (applyFixes && fixCandidates.Count > 0)
        {
            try
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);

                foreach (var (bill, targetNet) in fixCandidates)
                {
                    var changed = await AdjustApprovedVendorBillNetAmountAsync(
                        bill,
                        targetNet,
                        inventoryAccountId.Value,
                        payableAccountId.Value,
                        now,
                        userName,
                        cancellationToken);

                    if (changed)
                    {
                        billsUpdated++;
                    }
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _logger.LogError(ex, "QuickBooks AP bill repair failed for company {CompanyId}", companyId);
                return new VendorBillApRepairResult(
                    false,
                    ex.Message,
                    quickBooks.Bills.Count,
                    0,
                    missingInErp,
                    0,
                    quickBooks.ClosingBalance ?? 0m,
                    0m,
                    0m,
                    mismatches);
            }
        }

        var qbRefNos = quickBooks.Bills
            .Select(b => b.RefNo)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingInQuickBooks = erpBills.Count(b => !qbRefNos.Contains(b.RefNo!.Trim()));

        var qbClosing = quickBooks.ClosingBalance ?? 0m;
        var erpApStoredNet = await GetAccountBalanceAsync(companyId, AccountsPayable, cancellationToken);
        var qbDisplay = qbClosing == 0m ? 0m : Math.Round(Math.Abs(qbClosing), 2);
        var erpDisplay = Math.Round(Math.Abs(erpApStoredNet), 2);
        var diff = Math.Round(erpDisplay - qbDisplay, 2);

        var message = applyFixes
            ? billsUpdated > 0
                ? $"Updated {billsUpdated} vendor bill(s) to match QuickBooks AP amounts."
                : mismatches.Count == 0
                    ? "All matched QuickBooks AP bill amounts are already correct."
                    : "No bill amounts were changed."
            : $"Found {mismatches.Count} QuickBooks AP mismatch(es). Run with --apply to fix.";

        return new VendorBillApRepairResult(
            true,
            message,
            quickBooks.Bills.Count,
            billsUpdated,
            missingInErp,
            missingInQuickBooks,
            qbDisplay,
            erpDisplay,
            diff,
            mismatches);
    }

    private async Task<bool> AdjustApprovedVendorBillNetAmountAsync(
        VendorBill bill,
        decimal targetNetAmount,
        int inventoryAccountId,
        int payableAccountId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        targetNetAmount = Math.Round(targetNetAmount, 2);
        var delta = Math.Round(targetNetAmount - bill.NetAmount, 2);
        if (delta == 0m)
        {
            return false;
        }

        if (!bill.JournalEntryId.HasValue)
        {
            throw new InvalidOperationException(
                $"Vendor bill {bill.BillNumber} has no posted journal entry.");
        }

        var lines = await _unitOfWork.Repository<VendorBillLine>()
            .Query(asNoTracking: false)
            .Where(l => l.VendorBillId == bill.Id)
            .OrderBy(l => l.Id)
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            throw new InvalidOperationException(
                $"Vendor bill {bill.BillNumber} has no line items.");
        }

        var taxAmount = Math.Round(bill.TaxAmount, 2);
        var withholdingTaxAmount = Math.Round(bill.WithholdingTaxAmount, 2);
        var incomeTax236GAmount = Math.Round(bill.IncomeTax236GAmount, 2);
        var targetSubTotal = Math.Round(
            targetNetAmount - taxAmount + withholdingTaxAmount + incomeTax236GAmount,
            2);
        var currentSubTotal = Math.Round(lines.Sum(l => l.Amount), 2);
        var subDelta = Math.Round(targetSubTotal - currentSubTotal, 2);

        var adjustLine = lines[^1];
        adjustLine.Amount = Math.Round(adjustLine.Amount + subDelta, 2);
        if (adjustLine.Quantity > 0m)
        {
            adjustLine.Rate = Math.Round(adjustLine.Amount / adjustLine.Quantity, 2);
        }

        var inventoryTransactions = await _unitOfWork.Repository<InventoryTransaction>()
            .Query(asNoTracking: false)
            .Where(t => t.CompanyId == bill.CompanyId && t.ReferenceNo == bill.BillNumber)
            .ToListAsync(cancellationToken);

        if (adjustLine.ItemId.HasValue)
        {
            var matchingTxn = inventoryTransactions
                .FirstOrDefault(t => t.ItemId == adjustLine.ItemId.Value);
            if (matchingTxn is not null)
            {
                matchingTxn.UnitCost = adjustLine.Rate;
                matchingTxn.TotalCost = adjustLine.Amount;
                matchingTxn.UpdatedAt = now;
                matchingTxn.UpdatedBy = userName;
                _unitOfWork.Repository<InventoryTransaction>().Update(matchingTxn);
            }
        }

        bill.NetAmount = targetNetAmount;
        bill.UpdatedAt = now;
        bill.UpdatedBy = userName;
        _unitOfWork.Repository<VendorBill>().Update(bill);

        var journalLines = await _unitOfWork.Repository<JournalEntryLine>()
            .Query(asNoTracking: false)
            .Where(l => l.JournalEntryId == bill.JournalEntryId.Value)
            .ToListAsync(cancellationToken);

        var payableLine = journalLines.FirstOrDefault(l => l.ChartOfAccountId == payableAccountId)
            ?? throw new InvalidOperationException(
                $"Accounts Payable line not found for vendor bill {bill.BillNumber}.");

        foreach (var inventoryLine in journalLines.Where(l => l.ChartOfAccountId == inventoryAccountId).ToList())
        {
            _unitOfWork.Repository<JournalEntryLine>().Remove(inventoryLine);
        }

        await _unitOfWork.Repository<JournalEntryLine>().AddAsync(new JournalEntryLine
        {
            JournalEntryId = bill.JournalEntryId.Value,
            ChartOfAccountId = inventoryAccountId,
            Debit = targetSubTotal,
            Credit = 0m,
            Memo = "Inventory Asset"
        }, cancellationToken);

        payableLine.Credit = targetNetAmount;
        _unitOfWork.Repository<JournalEntryLine>().Update(payableLine);

        var journal = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .FirstAsync(j => j.Id == bill.JournalEntryId.Value, cancellationToken);
        journal.UpdatedAt = now;
        journal.UpdatedBy = userName;
        _unitOfWork.Repository<JournalEntry>().Update(journal);

        return true;
    }

    private async Task<Dictionary<(int ItemId, string? StackNo, string? LotNo), decimal>> BuildStackLotPurchaseRatesForRepairAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return new Dictionary<(int, string?, string?), decimal>();
        }

        var purchaseLines = await _unitOfWork.Repository<VendorBillLine>()
            .Query()
            .Where(l => l.VendorBill.CompanyId == companyId
                        && l.VendorBill.Status == BillStatus.Approved
                        && l.ItemId != null
                        && itemIds.Contains(l.ItemId.Value))
            .Select(l => new
            {
                ItemId = l.ItemId!.Value,
                StackNo = string.IsNullOrWhiteSpace(l.StackNo) ? l.Item!.StackNo : l.StackNo,
                LotNo = string.IsNullOrWhiteSpace(l.LotNo) ? l.Item!.LotNo : l.LotNo,
                l.Quantity,
                l.Rate
            })
            .ToListAsync(cancellationToken);

        var purchaseRatesByItem = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.PurchaseRate, cancellationToken);

        var rates = new Dictionary<(int, string?, string?), decimal>();
        foreach (var group in purchaseLines.GroupBy(x => (
                     x.ItemId,
                     StackNo: NormalizeStackLotKey(x.StackNo),
                     LotNo: NormalizeStackLotKey(x.LotNo))))
        {
            var ratedLines = group.Where(x => x.Rate > 0m).ToList();
            decimal weightedRate;
            if (ratedLines.Count > 0)
            {
                var ratedQty = ratedLines.Sum(x => x.Quantity);
                weightedRate = ratedQty > 0m
                    ? ratedLines.Sum(x => x.Quantity * x.Rate) / ratedQty
                    : 0m;
            }
            else
            {
                weightedRate = 0m;
            }

            if (weightedRate <= 0m
                && purchaseRatesByItem.TryGetValue(group.Key.ItemId, out var purchaseRate)
                && purchaseRate > 0m)
            {
                weightedRate = purchaseRate;
            }

            if (weightedRate > 0m)
            {
                rates[group.Key] = Math.Round(weightedRate, 2);
            }
        }

        return rates;
    }

    private static decimal ResolveStackLotRate(
        IReadOnlyDictionary<(int ItemId, string? StackNo, string? LotNo), decimal> stackLotRates,
        int itemId,
        string? stackNo,
        string? lotNo,
        decimal fallbackPurchaseRate)
    {
        var normalizedStack = NormalizeStackLotKey(stackNo);
        var normalizedLot = NormalizeStackLotKey(lotNo);
        if (stackLotRates.TryGetValue((itemId, normalizedStack, normalizedLot), out var rate) && rate > 0m)
        {
            return rate;
        }

        return Math.Round(fallbackPurchaseRate, 2);
    }

    private static string? NormalizeStackLotKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

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
