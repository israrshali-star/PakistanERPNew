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

public partial class VendorGlPostingService : IVendorGlPostingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<VendorGlPostingService> _logger;

    public VendorGlPostingService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        ILogger<VendorGlPostingService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<GlPostingResult> SyncVendorOpeningBalanceAsync(
        int vendorId,
        string vendorName,
        decimal openingBalance,
        DateTime? entryDate = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        await RemoveJournalByReferenceAsync(companyId, ReferenceTypes.Vendor, vendorId, cancellationToken);

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
                CreateLine(accounts.ApAccountId, amount, 0m, "Accounts Payable"),
                CreateLine(accounts.EquityAccountId, 0m, amount, "Opening balance offset")
            }
            : new List<JournalEntryLine>
            {
                CreateLine(accounts.EquityAccountId, amount, 0m, "Opening balance offset"),
                CreateLine(accounts.ApAccountId, 0m, amount, "Accounts Payable")
            };

        return await CreatePostedJournalAsync(
            companyId,
            (entryDate ?? DateTime.UtcNow).Date,
            $"Vendor opening balance — {vendorName.Trim()}",
            ReferenceTypes.Vendor,
            vendorId,
            lines,
            cancellationToken);
    }

    public Task<GlPostingResult> RemoveVendorOpeningBalanceAsync(
        int vendorId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        return RemoveJournalByReferenceAsync(companyId, ReferenceTypes.Vendor, vendorId, cancellationToken);
    }

    public Task<GlPostingResult> PostVendorPaymentAsync(
        VendorPayment payment,
        CancellationToken cancellationToken = default)
    {
        return SyncVendorPaymentAsync(
            payment,
            0m,
            null,
            payment.PaymentMethod,
            cancellationToken);
    }

    public async Task<GlPostingResult> SyncVendorPaymentAsync(
        VendorPayment payment,
        decimal previousAmount,
        int? previousBankId,
        PaymentMethod previousPaymentMethod,
        CancellationToken cancellationToken = default)
    {
        var companyId = payment.CompanyId;

        await RemoveJournalByReferenceAsync(companyId, ReferenceTypes.VendorPayment, payment.Id, cancellationToken);
        await ApplyBankBalanceChangeAsync(companyId, previousBankId, previousPaymentMethod, previousAmount, cancellationToken);

        if (payment.Amount <= 0m)
        {
            return new GlPostingResult(true, null);
        }

        var accounts = await ResolvePaymentAccountsAsync(companyId, payment, cancellationToken);
        if (!accounts.Success)
        {
            return new GlPostingResult(false, accounts.Message);
        }

        var partyName = await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.Id == payment.VendorId && v.CompanyId == companyId)
            .Select(v => v.VendorName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Vendor";
        partyName = partyName.Trim();

        var cashAccountId = await GetAccountIdAsync(companyId, CashInHand, cancellationToken);
        var paymentRef = payment.PaymentNumber.Trim();
        var creditMemo = cashAccountId.HasValue && accounts.CreditAccountId == cashAccountId.Value
            ? partyName
            : $"{partyName} — {paymentRef}";

        var amount = Math.Round(payment.Amount, 2);
        var lines = new List<JournalEntryLine>
        {
            CreateLine(accounts.ApAccountId, amount, 0m, partyName),
            CreateLine(accounts.CreditAccountId, 0m, amount, creditMemo)
        };

        var postResult = await CreatePostedJournalAsync(
            companyId,
            payment.PaymentDate,
            $"Vendor payment {payment.PaymentNumber}",
            ReferenceTypes.VendorPayment,
            payment.Id,
            lines,
            cancellationToken);

        if (!postResult.Success)
        {
            return postResult;
        }

        await ApplyBankBalanceChangeAsync(companyId, payment.BankId, payment.PaymentMethod, -amount, cancellationToken);
        return postResult;
    }

    public async Task<GlPostingResult> RemoveVendorPaymentAsync(
        int paymentId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var payment = await _unitOfWork.Repository<VendorPayment>()
            .Query()
            .Where(p => p.Id == paymentId && p.CompanyId == companyId)
            .Select(p => new { p.Amount, p.BankId, p.PaymentMethod })
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            return new GlPostingResult(false, "Payment not found.");
        }

        await RemoveJournalByReferenceAsync(companyId, ReferenceTypes.VendorPayment, paymentId, cancellationToken);
        await ApplyBankBalanceChangeAsync(companyId, payment.BankId, payment.PaymentMethod, payment.Amount, cancellationToken);

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
            return new GlPostingResult(false, "Could not post journal entry to Accounts Payable.");
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

    private async Task<(bool Success, string? Message, int ApAccountId, int EquityAccountId)>
        ResolveOpeningBalanceAccountsAsync(int companyId, CancellationToken cancellationToken)
    {
        var ap = await GetAccountIdAsync(companyId, AccountsPayable, cancellationToken);
        var equity = await GetAccountIdAsync(companyId, OpeningBalanceEquity, cancellationToken);

        if (ap is null)
        {
            return (false, $"Chart of account {AccountsPayable} (Account Payable) not found.", 0, 0);
        }

        if (equity is null)
        {
            return (false, $"Chart of account {OpeningBalanceEquity} (Opening Balance Equity) not found.", 0, 0);
        }

        return (true, null, ap.Value, equity.Value);
    }

    private async Task<(bool Success, string? Message, int ApAccountId, int CreditAccountId)>
        ResolvePaymentAccountsAsync(int companyId, VendorPayment payment, CancellationToken cancellationToken)
    {
        var ap = await GetAccountIdAsync(companyId, AccountsPayable, cancellationToken);
        if (ap is null)
        {
            return (false, $"Chart of account {AccountsPayable} (Account Payable) not found.", 0, 0);
        }

        if (payment.PaymentMethod == PaymentMethod.Cash || !payment.BankId.HasValue)
        {
            var cash = await GetAccountIdAsync(companyId, CashInHand, cancellationToken);
            if (cash is null)
            {
                return (false, $"Chart of account {CashInHand} (Cash In Hand) not found.", 0, 0);
            }

            return (true, null, ap.Value, cash.Value);
        }

        var bank = await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.Id == payment.BankId && b.CompanyId == companyId && b.IsActive)
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

        return (true, null, ap.Value, bank.ChartOfAccountId.Value);
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
