using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Services;

public partial class CustomerGlPostingService : ICustomerGlPostingService
{
    private const string AccountsReceivableNumber = "1200";
    private const string CashInHandNumber = "1100";
    private const string RetainedEarningsNumber = "3200";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CustomerGlPostingService> _logger;

    public CustomerGlPostingService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        ILogger<CustomerGlPostingService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<GlPostingResult> SyncCustomerOpeningBalanceAsync(
        int customerId,
        string buyerName,
        decimal openingBalance,
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
            DateTime.UtcNow.Date,
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
        CancellationToken cancellationToken = default)
    {
        return await SyncCustomerReceiptAsync(
            receipt,
            0m,
            null,
            receipt.PaymentMethod,
            cancellationToken);
    }

    public async Task<GlPostingResult> SyncCustomerReceiptAsync(
        CustomerReceipt receipt,
        decimal previousAmount,
        int? previousBankId,
        PaymentMethod previousPaymentMethod,
        CancellationToken cancellationToken = default)
    {
        var companyId = receipt.CompanyId;

        await RemoveJournalByReferenceAsync(companyId, ReferenceTypes.CustomerReceipt, receipt.Id, cancellationToken);
        await ApplyBankBalanceChangeAsync(companyId, previousBankId, previousPaymentMethod, -previousAmount, cancellationToken);

        if (receipt.Amount <= 0m)
        {
            return new GlPostingResult(true, null);
        }

        var accounts = await ResolveReceiptAccountsAsync(companyId, receipt, cancellationToken);
        if (!accounts.Success)
        {
            return new GlPostingResult(false, accounts.Message);
        }

        var amount = Math.Round(receipt.Amount, 2);
        var lines = new List<JournalEntryLine>
        {
            CreateLine(accounts.DebitAccountId, amount, 0m, accounts.DebitMemo),
            CreateLine(accounts.ArAccountId, 0m, amount, "Accounts Receivable")
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

        await ApplyBankBalanceChangeAsync(companyId, receipt.BankId, receipt.PaymentMethod, amount, cancellationToken);
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
            .Select(r => new { r.Amount, r.BankId, r.PaymentMethod })
            .FirstOrDefaultAsync(cancellationToken);

        if (receipt is null)
        {
            return new GlPostingResult(false, "Receipt not found.");
        }

        await RemoveJournalByReferenceAsync(companyId, ReferenceTypes.CustomerReceipt, receiptId, cancellationToken);
        await ApplyBankBalanceChangeAsync(companyId, receipt.BankId, receipt.PaymentMethod, -receipt.Amount, cancellationToken);

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
        decimal amountChange,
        CancellationToken cancellationToken)
    {
        if (amountChange == 0m || paymentMethod == PaymentMethod.Cash || !bankId.HasValue)
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
        var ar = await GetAccountIdAsync(companyId, AccountsReceivableNumber, cancellationToken);
        var equity = await GetAccountIdAsync(companyId, RetainedEarningsNumber, cancellationToken);

        if (ar is null)
        {
            return (false, $"Chart of account {AccountsReceivableNumber} (Accounts Receivable) not found.", 0, 0);
        }

        if (equity is null)
        {
            return (false, $"Chart of account {RetainedEarningsNumber} (Retained Earnings) not found.", 0, 0);
        }

        return (true, null, ar.Value, equity.Value);
    }

    private async Task<(bool Success, string? Message, int ArAccountId, int DebitAccountId, string DebitMemo)>
        ResolveReceiptAccountsAsync(int companyId, CustomerReceipt receipt, CancellationToken cancellationToken)
    {
        var ar = await GetAccountIdAsync(companyId, AccountsReceivableNumber, cancellationToken);
        if (ar is null)
        {
            return (false, $"Chart of account {AccountsReceivableNumber} (Accounts Receivable) not found.", 0, 0, string.Empty);
        }

        if (receipt.PaymentMethod == PaymentMethod.Cash || !receipt.BankId.HasValue)
        {
            var cash = await GetAccountIdAsync(companyId, CashInHandNumber, cancellationToken);
            if (cash is null)
            {
                return (false, $"Chart of account {CashInHandNumber} (Cash In Hand) not found.", 0, 0, string.Empty);
            }

            return (true, null, ar.Value, cash.Value, "Cash In Hand");
        }

        var bank = await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.Id == receipt.BankId && b.CompanyId == companyId && b.IsActive)
            .Select(b => new { b.BankName, b.ChartOfAccountId })
            .FirstOrDefaultAsync(cancellationToken);

        if (bank is null)
        {
            return (false, "Selected bank account is not valid.", 0, 0, string.Empty);
        }

        if (!bank.ChartOfAccountId.HasValue)
        {
            return (false, $"Bank \"{bank.BankName}\" is not linked to a chart of account.", 0, 0, string.Empty);
        }

        return (true, null, ar.Value, bank.ChartOfAccountId.Value, bank.BankName);
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
