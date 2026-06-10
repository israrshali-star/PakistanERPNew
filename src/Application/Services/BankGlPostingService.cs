using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Services;

public partial class BankGlPostingService : IBankGlPostingService
{
    private const int AssetsTypeId = 1;
    private const int CashAndBankSubTypeId = 1;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<BankGlPostingService> _logger;

    public BankGlPostingService(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        ILogger<BankGlPostingService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<int?> EnsureUndepositedFundsAccountAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetAccountIdAsync(companyId, UndepositedFunds, cancellationToken);
        if (existing.HasValue)
        {
            return existing;
        }

        var parentId = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.AccountNumber == BankAccountsParent && a.IsActive)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "system";
        var account = new ChartOfAccount
        {
            CompanyId = companyId,
            AccountNumber = UndepositedFunds,
            AccountName = "Undeposited Funds",
            TypeId = AssetsTypeId,
            SubTypeId = CashAndBankSubTypeId,
            ParentAccountId = parentId,
            IsActive = true,
            OpeningBalance = 0m,
            CreatedAt = now,
            CreatedBy = userName
        };

        await _unitOfWork.Repository<ChartOfAccount>().AddAsync(account, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return account.Id;
    }

    public async Task<decimal> GetAccountBalanceAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken = default)
    {
        var lines = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l => l.ChartOfAccountId == chartOfAccountId
                        && l.JournalEntry.CompanyId == companyId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && !l.JournalEntry.IsDeleted)
            .Select(l => new { l.Debit, l.Credit })
            .ToListAsync(cancellationToken);

        var debits = lines.Sum(l => l.Debit);
        var credits = lines.Sum(l => l.Credit);
        return Math.Round(debits - credits, 2);
    }

    public async Task<GlPostingResult> PostBankTransactionAsync(
        BankTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var companyId = transaction.CompanyId;
        await RemoveBankTransactionAsync(transaction.Id, cancellationToken);

        var amount = Math.Round(transaction.Amount, 2);
        if (amount <= 0m)
        {
            return new GlPostingResult(true, null);
        }

        var linesResult = await BuildJournalLinesAsync(transaction, amount, cancellationToken);
        if (!linesResult.Success)
        {
            return new GlPostingResult(false, linesResult.Message);
        }

        var description = BuildJournalDescription(transaction);
        var postResult = await CreatePostedJournalAsync(
            companyId,
            transaction.TransactionDate,
            description,
            ReferenceTypes.BankTransaction,
            transaction.Id,
            linesResult.Lines!,
            cancellationToken);

        if (!postResult.Success)
        {
            return new GlPostingResult(false, postResult.Message);
        }

        if (postResult.JournalEntryId.HasValue)
        {
            transaction.JournalEntryId = postResult.JournalEntryId;
            _unitOfWork.Repository<BankTransaction>().Update(transaction);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new GlPostingResult(true, null);
    }

    public async Task<GlPostingResult> RemoveBankTransactionAsync(
        int bankTransactionId,
        CancellationToken cancellationToken = default)
    {
        var entries = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Where(j => j.ReferenceType == ReferenceTypes.BankTransaction
                        && j.ReferenceId == bankTransactionId
                        && j.Status == JournalStatus.Posted
                        && !j.IsDeleted)
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

    private async Task<(bool Success, string? Message, List<JournalEntryLine>? Lines)> BuildJournalLinesAsync(
        BankTransaction transaction,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var companyId = transaction.CompanyId;

        switch (transaction.TransactionType)
        {
            case BankTransactionType.Deposit:
            {
                var undepositedId = await EnsureUndepositedFundsAccountAsync(companyId, cancellationToken);
                if (!undepositedId.HasValue)
                {
                    return (false, "Undeposited Funds account could not be created.", null);
                }

                var undepositedBalance = await GetAccountBalanceAsync(companyId, undepositedId.Value, cancellationToken);
                if (undepositedBalance < amount)
                {
                    return (
                        false,
                        $"Insufficient undeposited funds. Available: {undepositedBalance:N2}",
                        null);
                }

                var bankLabel = await GetAccountLabelAsync(transaction.ChartOfAccountId, cancellationToken);
                return (true, null,
                [
                    CreateLine(transaction.ChartOfAccountId, amount, 0m, $"Deposit — {bankLabel}"),
                    CreateLine(undepositedId.Value, 0m, amount, "Undeposited Funds")
                ]);
            }

            case BankTransactionType.Withdrawal:
            {
                if (!transaction.CounterChartOfAccountId.HasValue)
                {
                    return (false, "Pay-to account is required for write cheque.", null);
                }

                var bankBalance = await GetAccountBalanceAsync(companyId, transaction.ChartOfAccountId, cancellationToken);
                if (bankBalance < amount)
                {
                    return (false, $"Insufficient bank balance. Available: {bankBalance:N2}", null);
                }

                var party = string.IsNullOrWhiteSpace(transaction.PartyName)
                    ? "Cheque payment"
                    : transaction.PartyName.Trim();
                var chequeRef = string.IsNullOrWhiteSpace(transaction.ChequeNumber)
                    ? party
                    : $"{party} — Chq #{transaction.ChequeNumber.Trim()}";

                return (true, null,
                [
                    CreateLine(transaction.CounterChartOfAccountId.Value, amount, 0m, chequeRef),
                    CreateLine(transaction.ChartOfAccountId, 0m, amount, chequeRef)
                ]);
            }

            case BankTransactionType.Transfer:
            {
                if (!transaction.TransferToChartOfAccountId.HasValue)
                {
                    return (false, "Transfer destination account is required.", null);
                }

                if (transaction.TransferToChartOfAccountId == transaction.ChartOfAccountId)
                {
                    return (false, "Cannot transfer to the same account.", null);
                }

                var sourceBalance = await GetAccountBalanceAsync(companyId, transaction.ChartOfAccountId, cancellationToken);
                if (sourceBalance < amount)
                {
                    return (false, $"Insufficient balance in source account. Available: {sourceBalance:N2}", null);
                }

                var fromLabel = await GetAccountLabelAsync(transaction.ChartOfAccountId, cancellationToken);
                var toLabel = await GetAccountLabelAsync(transaction.TransferToChartOfAccountId.Value, cancellationToken);
                var memo = $"Transfer {fromLabel} → {toLabel}";

                return (true, null,
                [
                    CreateLine(transaction.TransferToChartOfAccountId.Value, amount, 0m, memo),
                    CreateLine(transaction.ChartOfAccountId, 0m, amount, memo)
                ]);
            }

            default:
                return (false, "Unsupported bank transaction type.", null);
        }
    }

    private static string BuildJournalDescription(BankTransaction transaction) =>
        transaction.TransactionType switch
        {
            BankTransactionType.Deposit => "Bank deposit",
            BankTransactionType.Withdrawal => string.IsNullOrWhiteSpace(transaction.PartyName)
                ? "Cheque payment"
                : $"Cheque — {transaction.PartyName.Trim()}",
            BankTransactionType.Transfer => "Cash/bank transfer",
            _ => "Bank transaction"
        };

    private async Task<string> GetAccountLabelAsync(int chartOfAccountId, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.Id == chartOfAccountId)
            .Select(a => new { a.AccountNumber, a.AccountName })
            .FirstOrDefaultAsync(cancellationToken);

        return account is null ? "Account" : $"{account.AccountNumber} {account.AccountName}";
    }

    private async Task<(bool Success, string? Message, int? JournalEntryId)> CreatePostedJournalAsync(
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
            _logger.LogError(ex, "Failed to post bank journal for {ReferenceType} {ReferenceId}", referenceType, referenceId);
            return (false, "Could not post journal entry for bank transaction.", null);
        }

        return (true, null, journalEntry.Id);
    }

    private async Task<int?> GetAccountIdAsync(
        int companyId,
        string accountNumber,
        CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.AccountNumber == accountNumber && a.IsActive && !a.IsDeleted)
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
