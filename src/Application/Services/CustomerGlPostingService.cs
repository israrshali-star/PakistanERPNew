using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.Common.Constants;
using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Services;

public partial class CustomerGlPostingService : ICustomerGlPostingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IBankGlPostingService _bankGlPosting;
    private readonly ILogger<CustomerGlPostingService> _logger;

    public CustomerGlPostingService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IBankGlPostingService bankGlPosting,
        ILogger<CustomerGlPostingService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _bankGlPosting = bankGlPosting;
        _logger = logger;
    }

    public async Task<GlPostingResult> SyncCustomerOpeningBalanceAsync(
        int customerId,
        string buyerName,
        decimal openingBalance,
        DateTime? entryDate = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        await RemoveJournalByReferenceAsync(companyId, ReferenceTypes.Customer, customerId, cancellationToken);

        if (openingBalance == 0m)
        {
            return new GlPostingResult(true, null);
        }

        var accounts = await ResolveOpeningBalanceAccountsAsync(companyId, cancellationToken);
        if (!accounts.Success)
        {
            return new GlPostingResult(false, accounts.Message);
        }

        var amount = Math.Abs(Math.Round(openingBalance, 2));
        var lines = openingBalance > 0m
            ? new List<JournalEntryLine>
            {
                CreateLine(accounts.ArAccountId, amount, 0m, "Accounts Receivable"),
                CreateLine(accounts.EquityAccountId, 0m, amount, "Opening balance offset")
            }
            : new List<JournalEntryLine>
            {
                CreateLine(accounts.EquityAccountId, amount, 0m, "Opening balance offset"),
                CreateLine(accounts.ArAccountId, 0m, amount, "Accounts Receivable")
            };

        return await CreatePostedJournalAsync(
            companyId,
            (entryDate ?? DateTime.UtcNow).Date,
            $"Customer opening balance — {buyerName.Trim()}",
            ReferenceTypes.Customer,
            customerId,
            lines,
            cancellationToken);
    }

    public Task<GlPostingResult> RemoveCustomerOpeningBalanceAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        return RemoveJournalByReferenceAsync(companyId, ReferenceTypes.Customer, customerId, cancellationToken);
    }

    public async Task<GlPostingResult> PostCustomerReceiptAsync(
        CustomerReceipt receipt,
        bool postUnclearedOtherBankCheque = false,
        CancellationToken cancellationToken = default)
    {
        return await SyncCustomerReceiptAsync(
            receipt,
            0m,
            null,
            receipt.PaymentMethod,
            receipt.ChequeBankType,
            postUnclearedOtherBankCheque,
            cancellationToken);
    }

    public async Task<GlPostingResult> SyncCustomerReceiptAsync(
        CustomerReceipt receipt,
        decimal previousAmount,
        int? previousBankId,
        PaymentMethod previousPaymentMethod,
        ChequeBankType? previousChequeBankType,
        bool postUnclearedOtherBankCheque = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = receipt.CompanyId;

        await RemoveJournalByReferenceAsync(companyId, ReferenceTypes.CustomerReceipt, receipt.Id, cancellationToken);
        await ApplyBankBalanceChangeAsync(
            companyId,
            previousBankId,
            previousPaymentMethod,
            previousChequeBankType,
            -previousAmount,
            cancellationToken);

        if (receipt.Amount <= 0m)
        {
            return new GlPostingResult(true, null);
        }

        if (ShouldDeferOtherBankChequeGl(receipt) && !postUnclearedOtherBankCheque)
        {
            return new GlPostingResult(true, null);
        }

        var accounts = await ResolveReceiptAccountsAsync(companyId, receipt, cancellationToken);
        if (!accounts.Success)
        {
            return new GlPostingResult(false, accounts.Message);
        }

        var partyName = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.Id == receipt.CustomerId && c.CompanyId == companyId)
            .Select(c => c.BuyerName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Customer";
        partyName = partyName.Trim();

        var cashAccountId = await GetAccountIdAsync(companyId, CashInHand, cancellationToken);
        var receiptRef = receipt.ReceiptNumber.Trim();
        var debitMemo = BuildReceiptDebitMemo(receipt, partyName, receiptRef, cashAccountId, accounts.DebitAccountId);

        var amount = Math.Round(receipt.Amount, 2);
        var lines = new List<JournalEntryLine>
        {
            CreateLine(accounts.DebitAccountId, amount, 0m, debitMemo),
            CreateLine(accounts.ArAccountId, 0m, amount, partyName)
        };

        var postResult = await CreatePostedJournalAsync(
            companyId,
            receipt.ReceiptDate,
            $"Customer receipt {receipt.ReceiptNumber}",
            ReferenceTypes.CustomerReceipt,
            receipt.Id,
            lines,
            cancellationToken);

        if (!postResult.Success)
        {
            return postResult;
        }

        await ApplyBankBalanceChangeAsync(
            companyId,
            receipt.BankId,
            receipt.PaymentMethod,
            receipt.ChequeBankType,
            amount,
            cancellationToken);
        return postResult;
    }

    public async Task<GlPostingResult> PostChequeClearanceAsync(
        CustomerReceipt receipt,
        int bankChartOfAccountId,
        CancellationToken cancellationToken = default)
    {
        var companyId = receipt.CompanyId;

        if (!await IsValidClearanceBankCoaAsync(companyId, bankChartOfAccountId, cancellationToken))
        {
            return new GlPostingResult(false, "Selected bank account is not valid for cheque clearance.");
        }

        await RemoveJournalByReferenceAsync(companyId, ReferenceTypes.CustomerReceipt, receipt.Id, cancellationToken);

        var ar = await GetAccountIdAsync(companyId, AccountsReceivable, cancellationToken);
        if (ar is null)
        {
            return new GlPostingResult(false, $"Chart of account {AccountsReceivable} (Accounts Receivable) not found.");
        }

        var partyName = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.Id == receipt.CustomerId && c.CompanyId == companyId)
            .Select(c => c.BuyerName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Customer";
        partyName = partyName.Trim();

        var amount = Math.Round(receipt.Amount, 2);
        var chequeRef = string.IsNullOrWhiteSpace(receipt.ChequeNumber)
            ? receipt.ReceiptNumber.Trim()
            : receipt.ChequeNumber.Trim();

        var lines = new List<JournalEntryLine>
        {
            CreateLine(bankChartOfAccountId, amount, 0m, $"Cheque cleared — {chequeRef}"),
            CreateLine(ar.Value, 0m, amount, partyName)
        };

        var postResult = await CreatePostedJournalAsync(
            companyId,
            receipt.ClearedAt ?? DateTime.UtcNow,
            $"Cheque clearance {receipt.ReceiptNumber}",
            ReferenceTypes.CustomerReceipt,
            receipt.Id,
            lines,
            cancellationToken);

        if (!postResult.Success)
        {
            return postResult;
        }

        var bankId = await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.CompanyId == companyId && b.ChartOfAccountId == bankChartOfAccountId && !b.IsDeleted)
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (bankId.HasValue)
        {
            await ApplyBankBalanceChangeAsync(
                companyId,
                bankId,
                PaymentMethod.BankTransfer,
                null,
                amount,
                cancellationToken);
        }

        return postResult;
    }

    public async Task<GlPostingResult> PostChequeReturnAsync(
        CustomerReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        var companyId = receipt.CompanyId;
        var amount = Math.Round(receipt.Amount, 2);

        var hasActiveJournal = await _unitOfWork.Repository<JournalEntry>()
            .Query()
            .AnyAsync(j =>
                j.CompanyId == companyId
                && j.ReferenceType == ReferenceTypes.CustomerReceipt
                && j.ReferenceId == receipt.Id
                && j.Status == JournalStatus.Posted
                && !j.IsDeleted,
                cancellationToken);

        if (!hasActiveJournal
            && !receipt.IsDeposited
            && !CustomerReceiptBalanceRules.IsChequeCleared(receipt.Status, receipt.ClearedAt))
        {
            return new GlPostingResult(true, null);
        }

        var ar = await GetAccountIdAsync(companyId, AccountsReceivable, cancellationToken);
        if (ar is null)
        {
            return new GlPostingResult(false, $"Chart of account {AccountsReceivable} (Accounts Receivable) not found.");
        }

        var undepositedId = await _bankGlPosting.EnsureUndepositedFundsAccountAsync(companyId, cancellationToken);
        if (!undepositedId.HasValue)
        {
            return new GlPostingResult(false, "Undeposited Funds account could not be resolved.");
        }

        int? bankCoaId = null;
        int? bankIdForBalance = null;
        if (receipt.DepositedBankTransactionId.HasValue)
        {
            var deposit = await _unitOfWork.Repository<BankTransaction>()
                .Query()
                .Where(bt => bt.Id == receipt.DepositedBankTransactionId.Value && bt.CompanyId == companyId)
                .Select(bt => new { bt.ChartOfAccountId, bt.BankId })
                .FirstOrDefaultAsync(cancellationToken);

            if (deposit is not null)
            {
                bankCoaId = deposit.ChartOfAccountId;
                bankIdForBalance = deposit.BankId;
            }
        }

        if (!bankCoaId.HasValue && receipt.BankId.HasValue)
        {
            bankCoaId = await _unitOfWork.Repository<Bank>()
                .Query()
                .Where(b => b.Id == receipt.BankId.Value && b.CompanyId == companyId && !b.IsDeleted)
                .Select(b => (int?)b.ChartOfAccountId)
                .FirstOrDefaultAsync(cancellationToken);
            bankIdForBalance = receipt.BankId;
        }

        var chequeRef = string.IsNullOrWhiteSpace(receipt.ChequeNumber)
            ? receipt.ReceiptNumber.Trim()
            : receipt.ChequeNumber.Trim();

        List<JournalEntryLine> reversalLines;
        if (bankCoaId.HasValue
            && (receipt.IsDeposited || CustomerReceiptBalanceRules.IsChequeCleared(receipt.Status, receipt.ClearedAt)))
        {
            reversalLines =
            [
                CreateLine(ar.Value, amount, 0m, $"Cheque returned — {chequeRef}"),
                CreateLine(bankCoaId.Value, 0m, amount, $"Cheque returned — {chequeRef}")
            ];
        }
        else
        {
            reversalLines =
            [
                CreateLine(ar.Value, amount, 0m, $"Cheque returned — {chequeRef}"),
                CreateLine(undepositedId.Value, 0m, amount, $"Cheque returned — {chequeRef}")
            ];
        }

        await RemoveJournalByReferenceAsync(companyId, ReferenceTypes.CustomerReceipt, receipt.Id, cancellationToken);

        var postResult = await CreatePostedJournalAsync(
            companyId,
            DateTime.UtcNow,
            $"Cheque returned {receipt.ReceiptNumber}",
            ReferenceTypes.CustomerReceipt,
            receipt.Id,
            reversalLines,
            cancellationToken);

        if (!postResult.Success)
        {
            return postResult;
        }

        if (bankIdForBalance.HasValue
            && (receipt.IsDeposited || CustomerReceiptBalanceRules.IsChequeCleared(receipt.Status, receipt.ClearedAt)))
        {
            await ApplyBankBalanceChangeAsync(
                companyId,
                bankIdForBalance,
                PaymentMethod.BankTransfer,
                null,
                -amount,
                cancellationToken);
        }

        return postResult;
    }

    public async Task<GlPostingResult> RemoveCustomerReceiptAsync(
        int receiptId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var receipt = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => r.Id == receiptId && r.CompanyId == companyId)
            .Select(r => new { r.Amount, r.BankId, r.PaymentMethod, r.ChequeBankType, r.Status, r.ClearedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (receipt is null)
        {
            return new GlPostingResult(false, "Receipt not found.");
        }

        await RemoveJournalByReferenceAsync(companyId, ReferenceTypes.CustomerReceipt, receiptId, cancellationToken);

        if (CustomerReceiptBalanceRules.AffectsCustomerBalance(
                receipt.PaymentMethod,
                receipt.Status,
                receipt.ClearedAt))
        {
            await ApplyBankBalanceChangeAsync(
                companyId,
                receipt.BankId,
                receipt.PaymentMethod,
                receipt.ChequeBankType,
                -receipt.Amount,
                cancellationToken);
        }

        return new GlPostingResult(true, null);
    }

    private async Task<GlPostingResult> CreatePostedJournalAsync(
        int companyId,
        DateTime entryDate,
        string description,
        string referenceType,
        int referenceId,
        IReadOnlyList<JournalEntryLine> lines,
        CancellationToken cancellationToken)
    {
        var entryNumber = await GenerateNextJournalEntryNumberAsync(companyId, cancellationToken);
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "system";

        var journalEntry = new JournalEntry
        {
            CompanyId = companyId,
            EntryNumber = entryNumber,
            EntryDate = entryDate.Date,
            Description = description,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Status = JournalStatus.Posted,
            CreatedAt = now,
            CreatedBy = userName
        };

        try
        {
            await _unitOfWork.Repository<JournalEntry>().AddAsync(journalEntry, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var line in lines)
            {
                line.JournalEntryId = journalEntry.Id;
            }

            await _unitOfWork.Repository<JournalEntryLine>().AddRangeAsync(lines, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to post journal entry for {ReferenceType} {ReferenceId}", referenceType, referenceId);
            return new GlPostingResult(false, "Could not post journal entry to Accounts Receivable.");
        }

        return new GlPostingResult(true, null);
    }

    private async Task<GlPostingResult> RemoveJournalByReferenceAsync(
        int companyId,
        string referenceType,
        int referenceId,
        CancellationToken cancellationToken)
    {
        var entries = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Where(j => j.CompanyId == companyId
                        && j.ReferenceType == referenceType
                        && j.ReferenceId == referenceId
                        && j.Status == JournalStatus.Posted)
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
        {
            return new GlPostingResult(true, null);
        }

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName;

        foreach (var entry in entries)
        {
            entry.IsDeleted = true;
            entry.DeletedAt = now;
            entry.DeletedBy = userName;
            _unitOfWork.Repository<JournalEntry>().Update(entry);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new GlPostingResult(true, null);
    }

    private async Task ApplyBankBalanceChangeAsync(
        int companyId,
        int? bankId,
        PaymentMethod paymentMethod,
        ChequeBankType? chequeBankType,
        decimal amountChange,
        CancellationToken cancellationToken)
    {
        if (amountChange == 0m || !UsesBankLedger(paymentMethod, chequeBankType) || !bankId.HasValue)
        {
            return;
        }

        var bank = await _unitOfWork.Repository<Bank>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(b => b.Id == bankId.Value && b.CompanyId == companyId, cancellationToken);

        if (bank is null)
        {
            return;
        }

        bank.CurrentBalance += amountChange;
        bank.UpdatedAt = DateTime.UtcNow;
        bank.UpdatedBy = _currentUser.UserName;
        _unitOfWork.Repository<Bank>().Update(bank);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<(bool Success, string? Message, int ArAccountId, int EquityAccountId)>
        ResolveOpeningBalanceAccountsAsync(int companyId, CancellationToken cancellationToken)
    {
        var ar = await GetAccountIdAsync(companyId, AccountsReceivable, cancellationToken);
        var equity = await GetAccountIdAsync(companyId, OpeningBalanceEquity, cancellationToken);

        if (ar is null)
        {
            return (false, $"Chart of account {AccountsReceivable} (Accounts Receivable) not found.", 0, 0);
        }

        if (equity is null)
        {
            return (false, $"Chart of account {OpeningBalanceEquity} (Opening Balance Equity) not found.", 0, 0);
        }

        return (true, null, ar.Value, equity.Value);
    }

    private async Task<(bool Success, string? Message, int ArAccountId, int DebitAccountId)>
        ResolveReceiptAccountsAsync(int companyId, CustomerReceipt receipt, CancellationToken cancellationToken)
    {
        var ar = await GetAccountIdAsync(companyId, AccountsReceivable, cancellationToken);
        if (ar is null)
        {
            return (false, $"Chart of account {AccountsReceivable} (Accounts Receivable) not found.", 0, 0);
        }

        if (receipt.PaymentMethod == PaymentMethod.Cash)
        {
            var cash = await GetAccountIdAsync(companyId, CashInHand, cancellationToken);
            if (cash is null)
            {
                return (false, $"Chart of account {CashInHand} (Cash In Hand) not found.", 0, 0);
            }

            return (true, null, ar.Value, cash.Value);
        }

        if (receipt.PaymentMethod == PaymentMethod.Cheque)
        {
            if (receipt.ChequeBankType == ChequeBankType.SameBank)
            {
                var sameBank = await _unitOfWork.Repository<Bank>()
                    .Query()
                    .Where(b => b.Id == receipt.BankId && b.CompanyId == companyId && b.IsActive)
                    .Select(b => new { b.BankName, b.ChartOfAccountId })
                    .FirstOrDefaultAsync(cancellationToken);

                if (sameBank is null)
                {
                    return (false, "Selected bank account is not valid.", 0, 0);
                }

                if (!sameBank.ChartOfAccountId.HasValue)
                {
                    return (false, $"Bank \"{sameBank.BankName}\" is not linked to a chart of account.", 0, 0);
                }

                return (true, null, ar.Value, sameBank.ChartOfAccountId.Value);
            }

            var undepositedId = await _bankGlPosting.EnsureUndepositedFundsAccountAsync(companyId, cancellationToken);
            if (!undepositedId.HasValue)
            {
                return (false, $"Could not create chart of account {UndepositedFunds} (Undeposited Funds).", 0, 0);
            }

            return (true, null, ar.Value, undepositedId.Value);
        }

        var bank = await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.Id == receipt.BankId && b.CompanyId == companyId && b.IsActive)
            .Select(b => new { b.BankName, b.ChartOfAccountId })
            .FirstOrDefaultAsync(cancellationToken);

        if (bank is null)
        {
            return (false, "Selected bank account is not valid.", 0, 0);
        }

        if (!bank.ChartOfAccountId.HasValue)
        {
            return (false, $"Bank \"{bank.BankName}\" is not linked to a chart of account.", 0, 0);
        }

        return (true, null, ar.Value, bank.ChartOfAccountId.Value);
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

    private static string BuildReceiptDebitMemo(
        CustomerReceipt receipt,
        string partyName,
        string receiptRef,
        int? cashAccountId,
        int debitAccountId)
    {
        if (receipt.PaymentMethod == PaymentMethod.Cheque)
        {
            var chequePart = !string.IsNullOrWhiteSpace(receipt.ChequeNumber)
                ? $"Chq #{receipt.ChequeNumber.Trim()}"
                : "Cheque";
            var postDated = receipt.ChequeDate.HasValue
                            && receipt.ChequeDate.Value.Date > receipt.ReceiptDate.Date;
            var memo = postDated
                ? $"{partyName} — {chequePart} (post-dated)"
                : $"{partyName} — {chequePart}";
            return memo;
        }

        return cashAccountId.HasValue && debitAccountId == cashAccountId.Value
            ? partyName
            : $"{partyName} — {receiptRef}";
    }

    private static bool UsesBankLedger(PaymentMethod paymentMethod, ChequeBankType? chequeBankType) =>
        paymentMethod == PaymentMethod.BankTransfer
        || (paymentMethod == PaymentMethod.Cheque && chequeBankType == ChequeBankType.SameBank);

    private static bool ShouldDeferOtherBankChequeGl(CustomerReceipt receipt) =>
        receipt.PaymentMethod == PaymentMethod.Cheque
        && receipt.ChequeBankType == ChequeBankType.OtherBank
        && !CustomerReceiptBalanceRules.IsChequeCleared(receipt.Status, receipt.ClearedAt);

    private async Task<bool> IsValidClearanceBankCoaAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        var cashInHand = await GetAccountIdAsync(companyId, CashInHand, cancellationToken);
        if (cashInHand.HasValue && cashInHand.Value == chartOfAccountId)
        {
            return true;
        }

        return await _unitOfWork.Repository<Bank>()
            .Query()
            .AnyAsync(
                b => b.CompanyId == companyId
                     && b.ChartOfAccountId == chartOfAccountId
                     && b.IsActive
                     && !b.IsDeleted,
                cancellationToken);
    }

    private static JournalEntryLine CreateLine(int accountId, decimal debit, decimal credit, string memo) =>
        new()
        {
            ChartOfAccountId = accountId,
            Debit = debit,
            Credit = credit,
            Memo = memo
        };

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
            var match = JournalEntryNumberRegex().Match(number);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var seq))
            {
                max = Math.Max(max, seq);
            }
        }

        return $"{prefix}{(max + 1):D4}";
    }

    [GeneratedRegex(@"^JE-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex JournalEntryNumberRegex();
}
