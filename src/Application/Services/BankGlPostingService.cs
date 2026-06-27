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
        var account = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.Id == chartOfAccountId && a.CompanyId == companyId)
            .Select(a => new { a.OpeningBalance, a.TypeId, a.AccountNumber })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return 0m;
        }

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
        return GlAccountBalance.ComputeNet(
            account.OpeningBalance,
            debits,
            credits,
            account.TypeId,
            account.AccountNumber);
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

        var payFromAccountNumber = await GetAccountNumberAsync(transaction.ChartOfAccountId, cancellationToken);
        var description = BuildJournalDescription(transaction, payFromAccountNumber);
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

        transaction.JournalEntryId = postResult.JournalEntryId;
        _unitOfWork.Repository<BankTransaction>().Update(transaction);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await ApplyLinkedBankBalanceAsync(companyId, transaction, amount, cancellationToken);

        return new GlPostingResult(true, null);
    }

    public async Task<GlPostingResult> RemoveBankTransactionAsync(
        int bankTransactionId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _unitOfWork.Repository<BankTransaction>()
            .Query()
            .FirstOrDefaultAsync(bt => bt.Id == bankTransactionId, cancellationToken);

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

        if (transaction is not null)
        {
            var amount = Math.Round(transaction.Amount, 2);
            if (amount > 0m)
            {
                await ApplyLinkedBankBalanceAsync(
                    transaction.CompanyId,
                    transaction,
                    -amount,
                    cancellationToken);
            }
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

                var party = await ResolvePartyNameAsync(transaction, cancellationToken);
                var payFromAccountNumber = await GetAccountNumberAsync(transaction.ChartOfAccountId, cancellationToken);
                var payFromMemo = BuildWithdrawalPayFromMemo(transaction, party, payFromAccountNumber);

                if (transaction.CustomerId.HasValue)
                {
                    var outstanding = await GetCustomerOutstandingBeforeTransactionAsync(
                        transaction,
                        cancellationToken);
                    var isDebitBalance = outstanding >= 0m;
                    transaction.CustomerBalanceEffect = isDebitBalance ? -amount : amount;

                    var arAccountId = await GetAccountIdAsync(companyId, AccountsReceivable, cancellationToken);
                    if (!arAccountId.HasValue)
                    {
                        return (false, $"Chart of account {AccountsReceivable} (Accounts Receivable) not found.", null);
                    }

                    var payFromBalance = await GetAccountBalanceAsync(companyId, transaction.ChartOfAccountId, cancellationToken);
                    if (payFromBalance < amount)
                    {
                        return (false, $"Insufficient balance in pay-from account. Available: {payFromBalance:N2}", null);
                    }

                    if (isDebitBalance)
                    {
                        return (true, null,
                        [
                            CreateLine(transaction.CounterChartOfAccountId.Value, 0m, amount, party),
                            CreateLine(transaction.ChartOfAccountId, amount, 0m, payFromMemo)
                        ]);
                    }

                    return (true, null,
                    [
                        CreateLine(arAccountId.Value, amount, 0m, party),
                        CreateLine(transaction.ChartOfAccountId, 0m, amount, payFromMemo)
                    ]);
                }

                transaction.CustomerBalanceEffect = 0m;

                var vendorPayFromBalance = await GetAccountBalanceAsync(companyId, transaction.ChartOfAccountId, cancellationToken);
                if (vendorPayFromBalance < amount)
                {
                    return (false, $"Insufficient balance in pay-from account. Available: {vendorPayFromBalance:N2}", null);
                }

                var counterAccountNumber = await GetAccountNumberAsync(
                    transaction.CounterChartOfAccountId.Value,
                    cancellationToken);

                if (TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId)
                    && (SalesTaxPaymentGlHelper.IsSalesTaxAccountNumber(counterAccountNumber)
                        || SalesTaxPaymentGlHelper.IsSalesTaxPartyName(transaction.PartyName)))
                {
                    return await BuildSplitSalesTaxPaymentLinesAsync(
                        transaction,
                        amount,
                        party,
                        payFromMemo,
                        counterAccountNumber,
                        cancellationToken);
                }

                return (true, null,
                [
                    CreateLine(transaction.CounterChartOfAccountId.Value, amount, 0m, party),
                    CreateLine(transaction.ChartOfAccountId, 0m, amount, payFromMemo)
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

    private async Task<(bool Success, string? Message, List<JournalEntryLine>? Lines)> BuildSplitSalesTaxPaymentLinesAsync(
        BankTransaction transaction,
        decimal amount,
        string party,
        string payFromMemo,
        string? counterAccountNumber,
        CancellationToken cancellationToken)
    {
        var companyId = transaction.CompanyId;
        var furtherTaxAccountId = await GetAccountIdAsync(companyId, FurtherTaxPayable, cancellationToken);
        var salesTax18AccountId = await GetAccountIdAsync(companyId, SalesTaxPayable18, cancellationToken);

        if (!furtherTaxAccountId.HasValue)
        {
            return (false, $"Chart of account {FurtherTaxPayable} (Further Tax Payable) not found.", null);
        }

        if (!salesTax18AccountId.HasValue)
        {
            return (false, $"Chart of account {SalesTaxPayable18} (Sales Tax Payable 18%) not found.", null);
        }

        decimal furtherPay;
        decimal salesTax18Pay;

        if (string.Equals(counterAccountNumber, FurtherTaxPayable, StringComparison.OrdinalIgnoreCase))
        {
            furtherPay = amount;
            salesTax18Pay = 0m;
        }
        else if (string.Equals(counterAccountNumber, SalesTaxPayable18, StringComparison.OrdinalIgnoreCase))
        {
            furtherPay = 0m;
            salesTax18Pay = amount;
        }
        else
        {
            var furtherBalance = await GetAccountBalanceAsync(companyId, furtherTaxAccountId.Value, cancellationToken);
            var salesTax18Balance = await GetAccountBalanceAsync(companyId, salesTax18AccountId.Value, cancellationToken);
            (furtherPay, salesTax18Pay) = SalesTaxPaymentGlHelper.AllocatePayment(
                amount,
                SalesTaxPaymentGlHelper.LiabilityOutstanding(furtherBalance),
                SalesTaxPaymentGlHelper.LiabilityOutstanding(salesTax18Balance));
        }

        var lines = new List<JournalEntryLine>();
        if (furtherPay > 0m)
        {
            lines.Add(CreateLine(
                furtherTaxAccountId.Value,
                0m,
                furtherPay,
                $"{party} — Further Tax (4%)"));
        }

        if (salesTax18Pay > 0m)
        {
            lines.Add(CreateLine(
                salesTax18AccountId.Value,
                0m,
                salesTax18Pay,
                $"{party} — Sales Tax (18%)"));
        }

        if (lines.Count == 0)
        {
            lines.Add(CreateLine(salesTax18AccountId.Value, 0m, amount, $"{party} — Sales Tax (18%)"));
        }

        lines.Add(CreateLine(transaction.ChartOfAccountId, 0m, amount, payFromMemo));
        return (true, null, lines);
    }

    private static string BuildJournalDescription(BankTransaction transaction, string? payFromAccountNumber = null)
    {
        var party = transaction.PartyName?.Trim() ?? "payment";

        return transaction.TransactionType switch
        {
            BankTransactionType.Deposit => "Bank deposit",
            BankTransactionType.Withdrawal => transaction.PaymentMethod switch
            {
                PaymentMethod.Cheque => $"Cheque — {party}",
                PaymentMethod.CashWithdrawal => "Cash withdrawal — Cash in Hand",
                PaymentMethod.Cash when IsCashWithdrawalFromBank(payFromAccountNumber)
                    => $"Cash withdrawal — {party}",
                PaymentMethod.Cash => $"Cash payment — {party}",
                PaymentMethod.BankTransfer => $"Bank transfer — {party}",
                _ => $"Payment — {party}"
            },
            BankTransactionType.Transfer => "Cash/bank transfer",
            _ => "Bank transaction"
        };
    }

    private static string BuildWithdrawalPayFromMemo(
        BankTransaction transaction,
        string party,
        string? payFromAccountNumber = null) =>
        transaction.PaymentMethod switch
        {
            PaymentMethod.Cheque when !string.IsNullOrWhiteSpace(transaction.ChequeNumber)
                => $"{party} — Chq #{transaction.ChequeNumber.Trim()}",
            PaymentMethod.CashWithdrawal when !string.IsNullOrWhiteSpace(transaction.ChequeNumber)
                => $"Cash in Hand — Chq #{transaction.ChequeNumber.Trim()}",
            PaymentMethod.BankTransfer => $"{party} — Bank transfer",
            PaymentMethod.Cash when IsCashWithdrawalFromBank(payFromAccountNumber)
                => $"{party} — Cash withdrawal",
            PaymentMethod.Cash => $"{party} — Cash",
            _ => party
        };

    private static bool IsCashWithdrawalFromBank(string? payFromAccountNumber) =>
        !string.IsNullOrWhiteSpace(payFromAccountNumber)
        && !string.Equals(payFromAccountNumber.Trim(), CashInHand, StringComparison.Ordinal);

    private async Task<decimal> GetCustomerOutstandingBeforeTransactionAsync(
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

    private async Task<string> ResolvePartyNameAsync(
        BankTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (transaction.CustomerId.HasValue)
        {
            var customerName = await _unitOfWork.Repository<Customer>()
                .Query()
                .Where(c => c.Id == transaction.CustomerId.Value)
                .Select(c => c.BuyerName)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(customerName))
            {
                return customerName.Trim();
            }
        }

        if (transaction.VendorId.HasValue)
        {
            var vendorName = await _unitOfWork.Repository<Vendor>()
                .Query()
                .Where(v => v.Id == transaction.VendorId.Value)
                .Select(v => v.VendorName)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(vendorName))
            {
                return vendorName.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(transaction.PartyName))
        {
            return transaction.PartyName.Trim();
        }

        if (transaction.CounterChartOfAccountId.HasValue)
        {
            var accountName = await _unitOfWork.Repository<ChartOfAccount>()
                .Query()
                .Where(a => a.Id == transaction.CounterChartOfAccountId.Value)
                .Select(a => a.AccountName)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(accountName))
            {
                return accountName.Trim();
            }
        }

        return "Payment";
    }

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

    private async Task<string?> GetAccountNumberAsync(
        int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.Id == chartOfAccountId)
            .Select(a => a.AccountNumber)
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

    private async Task ApplyLinkedBankBalanceAsync(
        int companyId,
        BankTransaction transaction,
        decimal amount,
        CancellationToken cancellationToken)
    {
        switch (transaction.TransactionType)
        {
            case BankTransactionType.Deposit:
                await AdjustBankBalanceByCoaAsync(companyId, transaction.ChartOfAccountId, amount, cancellationToken);
                break;
            case BankTransactionType.Withdrawal:
                await AdjustBankBalanceByCoaAsync(companyId, transaction.ChartOfAccountId, -amount, cancellationToken);
                break;
            case BankTransactionType.Transfer:
                await AdjustBankBalanceByCoaAsync(companyId, transaction.ChartOfAccountId, -amount, cancellationToken);
                if (transaction.TransferToChartOfAccountId.HasValue)
                {
                    await AdjustBankBalanceByCoaAsync(
                        companyId,
                        transaction.TransferToChartOfAccountId.Value,
                        amount,
                        cancellationToken);
                }

                break;
        }
    }

    private async Task AdjustBankBalanceByCoaAsync(
        int companyId,
        int chartOfAccountId,
        decimal delta,
        CancellationToken cancellationToken)
    {
        if (delta == 0m)
        {
            return;
        }

        var bank = await _unitOfWork.Repository<Bank>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(
                b => b.CompanyId == companyId && b.ChartOfAccountId == chartOfAccountId && !b.IsDeleted,
                cancellationToken);

        if (bank is null)
        {
            return;
        }

        bank.CurrentBalance = Math.Round(bank.CurrentBalance + delta, 2);
        bank.UpdatedAt = DateTime.UtcNow;
        bank.UpdatedBy = _currentUser.UserName;
        _unitOfWork.Repository<Bank>().Update(bank);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    [GeneratedRegex(@"^JE-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex JournalEntryNumberRegex();
}
