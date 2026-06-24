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
using System.Text.Json;

namespace PakistanAccountingERP.Application.Services;

public class BankTransactionService : IBankTransactionService
{
    private const int AssetsTypeId = 1;
    private const int CashAndBankSubTypeId = 1;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly IBankGlPostingService _bankGlPostingService;
    private readonly ICustomerGlPostingService _customerGlPostingService;
    private readonly ILogger<BankTransactionService> _logger;

    public BankTransactionService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        IBankGlPostingService bankGlPostingService,
        ICustomerGlPostingService customerGlPostingService,
        ILogger<BankTransactionService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _bankGlPostingService = bankGlPostingService;
        _customerGlPostingService = customerGlPostingService;
        _logger = logger;
    }

    public async Task<DataTableResponse<BankTransactionListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        int? bankId = null,
        BankTransactionType? transactionType = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<BankTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId);

        if (bankId.HasValue)
        {
            query = query.Where(t => t.BankId == bankId.Value);
        }

        if (transactionType.HasValue)
        {
            query = query.Where(t => t.TransactionType == transactionType.Value);
        }

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(t =>
                t.ChartOfAccount.AccountName.Contains(term)
                || t.ChartOfAccount.AccountNumber.Contains(term)
                || (t.Description != null && t.Description.Contains(term))
                || (t.PartyName != null && t.PartyName.Contains(term))
                || (t.ChequeNumber != null && t.ChequeNumber.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rawRows = await query
            .Select(t => new
            {
                t.Id,
                AccountLabel = t.ChartOfAccount.AccountNumber + " — " + t.ChartOfAccount.AccountName,
                t.TransactionDate,
                t.TransactionType,
                TransferToAccountLabel = t.TransferToChartOfAccount != null
                    ? t.TransferToChartOfAccount.AccountNumber + " — " + t.TransferToChartOfAccount.AccountName
                    : null,
                t.Amount,
                t.Description,
                t.PartyName,
                t.PaymentMethod,
                t.ChequeNumber,
                t.IsReconciled
            })
            .ToListAsync(cancellationToken);

        var rows = rawRows
            .Select(t => new BankTransactionListItemDto(
                t.Id,
                t.AccountLabel,
                t.TransactionDate,
                GetTransactionTypeLabel(t.TransactionType),
                t.TransferToAccountLabel,
                t.Amount,
                t.Description,
                t.PartyName,
                t.PaymentMethod?.ToString(),
                t.ChequeNumber,
                t.IsReconciled))
            .ToList();

        return new DataTableResponse<BankTransactionListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<BankTransactionDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<BankTransaction>()
            .Query()
            .Where(t => t.Id == id && t.CompanyId == companyId)
            .Select(t => new BankTransactionDto(
                t.Id,
                t.ChartOfAccountId,
                t.ChartOfAccount.AccountNumber + " — " + t.ChartOfAccount.AccountName,
                t.TransactionType,
                t.TransferToChartOfAccountId,
                t.TransferToChartOfAccount != null
                    ? t.TransferToChartOfAccount.AccountNumber + " — " + t.TransferToChartOfAccount.AccountName
                    : null,
                t.CounterChartOfAccountId,
                t.PartyName,
                t.PaymentMethod,
                t.TransactionDate,
                t.ChequeNumber,
                t.ChequeDate,
                t.Amount,
                t.Description,
                t.IsReconciled,
                t.JournalEntryId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BankCoaLookupDto>> GetBankCoaLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        return await QueryBankCoaAccountsAsync(companyId, includeCashInHand: false, cancellationToken);
    }

    public async Task<IReadOnlyList<BankCoaLookupDto>> GetTransferCoaLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        return await QueryBankCoaAccountsAsync(companyId, includeCashInHand: true, cancellationToken);
    }

    public async Task<IReadOnlyList<BankCoaLookupDto>> GetDepositCoaLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        return await QueryBankCoaAccountsAsync(companyId, includeCashInHand: true, cancellationToken);
    }

    public async Task<IReadOnlyList<WriteChequePartyLookupDto>> GetWriteChequePartyLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        const int arSubTypeId = 2;
        const int apSubTypeId = 8;

        var accounts = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.IsActive && !a.IsDeleted)
            .Select(a => new
            {
                a.Id,
                a.AccountNumber,
                a.AccountName,
                a.SubTypeId,
                a.ParentAccountId,
                ParentNumber = a.ParentAccount != null ? a.ParentAccount.AccountNumber : null
            })
            .ToListAsync(cancellationToken);

        var arAccountIds = accounts
            .Where(a => a.SubTypeId == arSubTypeId
                        || a.AccountNumber is AccountsReceivable or "11000"
                        || a.ParentNumber is AccountsReceivable or "11000")
            .Select(a => a.Id)
            .ToHashSet();

        var apAccountIds = accounts
            .Where(a => a.SubTypeId == apSubTypeId
                        || a.AccountNumber == AccountsPayable
                        || a.ParentNumber == AccountsPayable)
            .Select(a => a.Id)
            .ToHashSet();

        var defaultArId = accounts
            .Where(a => a.AccountNumber == AccountsReceivable)
            .Select(a => (int?)a.Id)
            .FirstOrDefault()
            ?? accounts.Where(a => arAccountIds.Contains(a.Id)).Select(a => (int?)a.Id).FirstOrDefault();

        var defaultApId = accounts
            .Where(a => a.AccountNumber == AccountsPayable)
            .Select(a => (int?)a.Id)
            .FirstOrDefault()
            ?? accounts.Where(a => apAccountIds.Contains(a.Id)).Select(a => (int?)a.Id).FirstOrDefault();

        var result = new List<WriteChequePartyLookupDto>();
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddParty(
            int chartOfAccountId,
            string partyType,
            int? customerId,
            int? vendorId,
            string partyName,
            string accountNumber,
            string? partyCode = null)
        {
            var key = $"{chartOfAccountId}:{customerId}:{vendorId}";
            if (!usedKeys.Add(key))
            {
                return;
            }

            result.Add(new WriteChequePartyLookupDto(
                chartOfAccountId,
                partyType,
                customerId,
                vendorId,
                partyName,
                accountNumber,
                0m,
                partyCode));
        }

        foreach (var account in accounts.Where(a => arAccountIds.Contains(a.Id)))
        {
            if (IsGenericArApName(account.AccountName))
            {
                continue;
            }

            AddParty(account.Id, "AR", null, null, account.AccountName, account.AccountNumber);
        }

        foreach (var account in accounts.Where(a => apAccountIds.Contains(a.Id)))
        {
            if (IsGenericArApName(account.AccountName))
            {
                continue;
            }

            AddParty(account.Id, "AP", null, null, account.AccountName, account.AccountNumber);
        }

        var customers = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.IsActive && !c.IsDeleted)
            .OrderBy(c => c.BuyerName)
            .Select(c => new { c.Id, c.BuyerName, c.BuyerId })
            .ToListAsync(cancellationToken);

        foreach (var customer in customers)
        {
            var matched = accounts.FirstOrDefault(a =>
                arAccountIds.Contains(a.Id)
                && !IsGenericArApName(a.AccountName)
                && string.Equals(a.AccountName.Trim(), customer.BuyerName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (matched is not null)
            {
                AddParty(matched.Id, "AR", customer.Id, null, customer.BuyerName, matched.AccountNumber, customer.BuyerId);
                continue;
            }

            if (!defaultArId.HasValue)
            {
                continue;
            }

            var arNumber = accounts.First(a => a.Id == defaultArId.Value).AccountNumber;
            AddParty(defaultArId.Value, "AR", customer.Id, null, customer.BuyerName, arNumber, customer.BuyerId);
        }

        var vendors = await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId && v.IsActive && !v.IsDeleted)
            .OrderBy(v => v.VendorName)
            .Select(v => new { v.Id, v.VendorName, v.VendorCode })
            .ToListAsync(cancellationToken);

        foreach (var vendor in vendors)
        {
            var matched = accounts.FirstOrDefault(a =>
                apAccountIds.Contains(a.Id)
                && !IsGenericArApName(a.AccountName)
                && string.Equals(a.AccountName.Trim(), vendor.VendorName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (matched is not null)
            {
                AddParty(matched.Id, "AP", null, vendor.Id, vendor.VendorName, matched.AccountNumber, vendor.VendorCode);
                continue;
            }

            if (!defaultApId.HasValue)
            {
                continue;
            }

            var apNumber = accounts.First(a => a.Id == defaultApId.Value).AccountNumber;
            AddParty(defaultApId.Value, "AP", null, vendor.Id, vendor.VendorName, apNumber, vendor.VendorCode);
        }

        var payFromIds = (await QueryBankCoaAccountsAsync(companyId, includeCashInHand: true, cancellationToken))
            .Select(a => a.Id)
            .ToHashSet();

        var standaloneCoaIds = result
            .Where(r => r.CustomerId is null && r.VendorId is null)
            .Select(r => r.ChartOfAccountId)
            .ToHashSet();

        var otherLeafAccounts = accounts
            .Where(a => !payFromIds.Contains(a.Id) && !standaloneCoaIds.Contains(a.Id))
            .OrderBy(a => a.AccountNumber)
            .ToList();

        foreach (var account in otherLeafAccounts)
        {
            var isLeaf = !accounts.Any(child => child.ParentAccountId == account.Id);
            if (!isLeaf)
            {
                continue;
            }

            AddParty(account.Id, "COA", null, null, account.AccountName, account.AccountNumber);
        }

        var cashInHandAccount = accounts.FirstOrDefault(a => a.AccountNumber == CashInHand);
        if (cashInHandAccount is not null)
        {
            AddParty(
                cashInHandAccount.Id,
                "CASH",
                null,
                null,
                cashInHandAccount.AccountName,
                cashInHandAccount.AccountNumber);
        }

        var withBalances = new List<WriteChequePartyLookupDto>();
        foreach (var item in result)
        {
            var balance = item.CustomerId.HasValue
                ? await GetCustomerOutstandingAsync(companyId, item.CustomerId.Value, cancellationToken)
                : item.VendorId.HasValue
                    ? await GetVendorOutstandingAsync(companyId, item.VendorId.Value, cancellationToken)
                    : await _bankGlPostingService.GetAccountBalanceAsync(companyId, item.ChartOfAccountId, cancellationToken);
            withBalances.Add(item with { Balance = balance });
        }

        return withBalances
            .OrderBy(r => r.PartyType)
            .ThenBy(r => r.PartyName)
            .ToList();
    }

    private static bool IsGenericArApName(string accountName) =>
        accountName.Contains("Accounts Receivable", StringComparison.OrdinalIgnoreCase)
        || accountName.Contains("Accounts Payable", StringComparison.OrdinalIgnoreCase)
        || accountName.Equals("Account Payable", StringComparison.OrdinalIgnoreCase);

    public async Task<BankUndepositedSummaryDto> GetUndepositedSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var undepositedId = await _bankGlPostingService.EnsureUndepositedFundsAccountAsync(companyId, cancellationToken);
        if (!undepositedId.HasValue)
        {
            return new BankUndepositedSummaryDto(0m, UndepositedFunds);
        }

        var balance = await _bankGlPostingService.GetAccountBalanceAsync(companyId, undepositedId.Value, cancellationToken);
        return new BankUndepositedSummaryDto(balance, UndepositedFunds);
    }

    public async Task<IReadOnlyList<UndepositedChequeDto>> GetUndepositedChequesAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var today = DateTime.UtcNow.Date;

        return await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r =>
                r.CompanyId == companyId
                && !r.IsDeleted
                && r.PaymentMethod == PaymentMethod.Cheque
                && r.ChequeBankType != ChequeBankType.SameBank
                && r.ClearedAt == null
                && !r.IsDeposited
                && r.Amount > 0m)
            .OrderBy(r => r.ReceiptDate)
            .ThenBy(r => r.ReceiptNumber)
            .Select(r => new UndepositedChequeDto(
                r.Id,
                r.Customer.BuyerName,
                r.ReceiptNumber,
                r.ChequeNumber,
                r.Amount,
                r.ReceiptDate,
                r.ChequeDate,
                r.ChequeDate.HasValue && r.ChequeDate.Value.Date > today))
            .ToListAsync(cancellationToken);
    }

    public async Task<BankTransactionSaveResult> CreateAsync(
        BankTransactionSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var validation = await ValidateSaveRequestAsync(companyId, request, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        IReadOnlyList<CustomerReceipt>? depositReceipts = null;
        if (request.TransactionType == BankTransactionType.Deposit)
        {
            var depositLoad = await LoadUndepositedChequeReceiptsAsync(
                companyId,
                request.CustomerReceiptIds,
                request.TransactionDate,
                cancellationToken);
            if (depositLoad.Receipts is null)
            {
                return new BankTransactionSaveResult(
                    false,
                    depositLoad.ErrorMessage ?? "One or more selected cheques are invalid or already deposited.",
                    null);
            }

            depositReceipts = depositLoad.Receipts;

            request.Amount = depositReceipts.Sum(r => r.Amount);
        }

        var bankId = await ResolveBankIdForCoaAsync(companyId, request.ChartOfAccountId, cancellationToken);
        if (!bankId.HasValue)
        {
            return new BankTransactionSaveResult(false, "Could not resolve bank record for the selected account.", null);
        }

        int? transferBankId = null;
        if (request.TransactionType == BankTransactionType.Transfer && request.TransferToChartOfAccountId.HasValue)
        {
            transferBankId = await ResolveBankIdForCoaAsync(
                companyId,
                request.TransferToChartOfAccountId.Value,
                cancellationToken);
        }

        var now = DateTime.UtcNow;
        var partyName = request.PartyName?.Trim();
        string? chequeNumber = null;
        DateTime? chequeDate = null;

        if (request.TransactionType == BankTransactionType.Withdrawal)
        {
            partyName = await ResolvePartyNameForSaveAsync(
                companyId,
                request.CustomerId,
                request.VendorId,
                request.CounterChartOfAccountId,
                partyName,
                cancellationToken);

            if (request.PaymentMethod is PaymentMethod.Cheque or PaymentMethod.CashWithdrawal)
            {
                chequeNumber = await ResolveChequeNumberForWithdrawalAsync(
                    companyId,
                    bankId.Value,
                    request.ChartOfAccountId,
                    request.ChequeNumber?.Trim(),
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(chequeNumber))
                {
                    return new BankTransactionSaveResult(
                        false,
                        "Cheque number is required. Enter a cheque # or save a starting sequence for this bank.",
                        null);
                }

                var duplicateCheque = await _unitOfWork.Repository<BankTransaction>()
                    .Query()
                    .AnyAsync(bt =>
                        bt.CompanyId == companyId
                        && bt.ChartOfAccountId == request.ChartOfAccountId
                        && bt.TransactionType == BankTransactionType.Withdrawal
                        && !bt.IsDeleted
                        && bt.ChequeNumber == chequeNumber,
                        cancellationToken);

                if (duplicateCheque)
                {
                    return new BankTransactionSaveResult(
                        false,
                        "This cheque number is already used for the selected bank account.",
                        null);
                }

                chequeDate = request.ChequeDate?.Date;
            }
        }

        var entity = new BankTransaction
        {
            CompanyId = companyId,
            BankId = bankId.Value,
            ChartOfAccountId = request.ChartOfAccountId,
            TransactionType = request.TransactionType,
            TransferToBankId = transferBankId,
            TransferToChartOfAccountId = request.TransferToChartOfAccountId,
            CounterChartOfAccountId = request.CounterChartOfAccountId,
            CustomerId = request.CustomerId,
            VendorId = request.VendorId,
            PaymentMethod = request.TransactionType == BankTransactionType.Withdrawal
                ? request.PaymentMethod
                : null,
            PartyName = partyName,
            TransactionDate = request.TransactionDate.Date,
            ChequeNumber = chequeNumber,
            ChequeDate = chequeDate,
            Amount = request.Amount,
            Description = request.Description?.Trim(),
            IsReconciled = false,
            CreatedAt = now,
            CreatedBy = _currentUser.UserName
        };

        var useTransaction = depositReceipts is { Count: > 0 };
        try
        {
            if (useTransaction)
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
            }

            await _unitOfWork.Repository<BankTransaction>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (depositReceipts is { Count: > 0 })
            {
                foreach (var receipt in depositReceipts)
                {
                    if (!await HasPostedReceiptJournalAsync(companyId, receipt.Id, cancellationToken))
                    {
                        var receiveGl = await _customerGlPostingService.PostCustomerReceiptAsync(
                            receipt,
                            postUnclearedOtherBankCheque: true,
                            cancellationToken);
                        if (!receiveGl.Success)
                        {
                            if (useTransaction)
                            {
                                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                            }
                            else
                            {
                                _unitOfWork.Repository<BankTransaction>().Remove(entity);
                                await _unitOfWork.SaveChangesAsync(cancellationToken);
                            }

                            return new BankTransactionSaveResult(false, receiveGl.Message, null);
                        }
                    }
                }
            }

            var glResult = await _bankGlPostingService.PostBankTransactionAsync(entity, cancellationToken);
            if (!glResult.Success)
            {
                if (useTransaction)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                }
                else
                {
                    _unitOfWork.Repository<BankTransaction>().Remove(entity);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                return new BankTransactionSaveResult(false, glResult.Message, null);
            }

            if (depositReceipts is { Count: > 0 })
            {
                await MarkChequesDepositedAsync(depositReceipts, entity.Id, cancellationToken);
            }

            if (useTransaction)
            {
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }
        }
        catch (DbUpdateException ex)
        {
            if (useTransaction)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            }

            _logger.LogError(ex, "Failed to create bank transaction");
            return new BankTransactionSaveResult(false, "Could not save bank transaction.", null);
        }

        await TryAuditAsync("Create", entity.Id.ToString(), null, JsonSerializer.Serialize(request), cancellationToken);

        string? nextChequeNumber = null;
        if (request.TransactionType == BankTransactionType.Withdrawal
            && request.PaymentMethod is PaymentMethod.Cheque or PaymentMethod.CashWithdrawal
            && !string.IsNullOrWhiteSpace(entity.ChequeNumber))
        {
            nextChequeNumber = await AdvanceNextChequeNumberAsync(
                companyId,
                bankId.Value,
                entity.ChequeNumber,
                cancellationToken);
        }

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new BankTransactionSaveResult(true, null, dto, nextChequeNumber);
    }

    public async Task<BankNextChequeNumberDto> GetNextChequeNumberAsync(
        int chartOfAccountId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        if (chartOfAccountId <= 0)
        {
            return new BankNextChequeNumberDto(null, false);
        }

        if (!await IsValidBankCoaAsync(companyId, chartOfAccountId, cancellationToken))
        {
            return new BankNextChequeNumberDto(null, false);
        }

        var bankId = await ResolveBankIdForCoaAsync(companyId, chartOfAccountId, cancellationToken);
        if (!bankId.HasValue)
        {
            return new BankNextChequeNumberDto(null, false);
        }

        var nextChequeNumber = await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.Id == bankId.Value && b.CompanyId == companyId)
            .Select(b => b.NextChequeNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(nextChequeNumber))
        {
            nextChequeNumber = await DeriveNextChequeNumberFromHistoryAsync(
                companyId,
                chartOfAccountId,
                cancellationToken);
        }

        return new BankNextChequeNumberDto(
            nextChequeNumber,
            !string.IsNullOrWhiteSpace(nextChequeNumber));
    }

    public async Task<BankNextChequeNumberSaveResult> SetNextChequeNumberAsync(
        BankNextChequeNumberSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return new BankNextChequeNumberSaveResult(false, companyError!.Message, null);
        }

        if (request.ChartOfAccountId <= 0)
        {
            return new BankNextChequeNumberSaveResult(false, "Bank account is required.", null);
        }

        var nextChequeNumber = request.NextChequeNumber?.Trim();
        if (string.IsNullOrWhiteSpace(nextChequeNumber))
        {
            return new BankNextChequeNumberSaveResult(false, "Starting cheque number is required.", null);
        }

        if (nextChequeNumber.Length > 50)
        {
            return new BankNextChequeNumberSaveResult(false, "Cheque number cannot exceed 50 characters.", null);
        }

        if (!await IsValidBankCoaAsync(companyId, request.ChartOfAccountId, cancellationToken))
        {
            return new BankNextChequeNumberSaveResult(false, "Select a valid bank account from Chart of Accounts.", null);
        }

        var bankId = await ResolveBankIdForCoaAsync(companyId, request.ChartOfAccountId, cancellationToken);
        if (!bankId.HasValue)
        {
            return new BankNextChequeNumberSaveResult(false, "Could not resolve bank record for the selected account.", null);
        }

        var bank = await _unitOfWork.Repository<Bank>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(b => b.Id == bankId.Value && b.CompanyId == companyId, cancellationToken);

        if (bank is null)
        {
            return new BankNextChequeNumberSaveResult(false, "Bank account not found.", null);
        }

        bank.NextChequeNumber = nextChequeNumber;
        bank.UpdatedAt = DateTime.UtcNow;
        bank.UpdatedBy = _currentUser.UserName ?? "system";
        _unitOfWork.Repository<Bank>().Update(bank);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to save next cheque number for bank {BankId}", bankId);
            return new BankNextChequeNumberSaveResult(false, "Could not save starting cheque number.", null);
        }

        return new BankNextChequeNumberSaveResult(
            true,
            null,
            new BankNextChequeNumberDto(nextChequeNumber, true));
    }

    private async Task<string?> AdvanceNextChequeNumberAsync(
        int companyId,
        int bankId,
        string usedChequeNumber,
        CancellationToken cancellationToken)
    {
        var bank = await _unitOfWork.Repository<Bank>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(b => b.Id == bankId && b.CompanyId == companyId, cancellationToken);

        if (bank is null)
        {
            return null;
        }

        var next = ChequeNumberHelper.Increment(usedChequeNumber.Trim());
        if (string.IsNullOrWhiteSpace(next))
        {
            return null;
        }

        bank.NextChequeNumber = next;
        bank.UpdatedAt = DateTime.UtcNow;
        bank.UpdatedBy = _currentUser.UserName ?? "system";
        _unitOfWork.Repository<Bank>().Update(bank);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return next;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Failed to advance next cheque number for bank {BankId}", bankId);
            return null;
        }
    }

    private async Task<BankTransactionSaveResult> ValidateSaveRequestAsync(
        int companyId,
        BankTransactionSaveRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ChartOfAccountId <= 0)
        {
            return new BankTransactionSaveResult(false, "Account is required.", null);
        }

        if (request.TransactionDate == default)
        {
            return new BankTransactionSaveResult(false, "Transaction date is required.", null);
        }

        if (request.TransactionType != BankTransactionType.Deposit && request.Amount <= 0)
        {
            return new BankTransactionSaveResult(false, "Amount must be greater than zero.", null);
        }

        switch (request.TransactionType)
        {
            case BankTransactionType.Deposit:
                if (request.CustomerReceiptIds is null || request.CustomerReceiptIds.Count == 0)
                {
                    return new BankTransactionSaveResult(false, "Select at least one cheque to deposit.", null);
                }

                if (!await IsValidDepositCoaAsync(companyId, request.ChartOfAccountId, cancellationToken))
                {
                    return new BankTransactionSaveResult(
                        false,
                        "Select a valid bank or Cash in Hand account from Chart of Accounts.",
                        null);
                }

                break;

            case BankTransactionType.Withdrawal:
                if (!request.PaymentMethod.HasValue)
                {
                    return new BankTransactionSaveResult(false, "Payment method is required.", null);
                }

                switch (request.PaymentMethod.Value)
                {
                    case PaymentMethod.Cash:
                        if (!await IsValidCashPayFromCoaAsync(companyId, request.ChartOfAccountId, cancellationToken))
                        {
                            return new BankTransactionSaveResult(
                                false,
                                "Select Cash in Hand or a bank account as the pay-from account.",
                                null);
                        }

                        break;

                    case PaymentMethod.Cheque:
                    case PaymentMethod.BankTransfer:
                        if (!await IsValidBankCoaAsync(companyId, request.ChartOfAccountId, cancellationToken))
                        {
                            return new BankTransactionSaveResult(false, "Select a valid bank account from Chart of Accounts.", null);
                        }

                        break;

                    case PaymentMethod.CashWithdrawal:
                        if (!await IsValidBankCoaAsync(companyId, request.ChartOfAccountId, cancellationToken))
                        {
                            return new BankTransactionSaveResult(false, "Select a valid bank account from Chart of Accounts.", null);
                        }

                        if (!request.CounterChartOfAccountId.HasValue
                            || !await IsCashInHandCoaAsync(companyId, request.CounterChartOfAccountId.Value, cancellationToken))
                        {
                            return new BankTransactionSaveResult(false, "Cash withdrawal must credit Cash in Hand.", null);
                        }

                        if (request.CustomerId.HasValue || request.VendorId.HasValue)
                        {
                            return new BankTransactionSaveResult(false, "Cash withdrawal cannot be linked to a customer or vendor.", null);
                        }

                        break;

                    default:
                        return new BankTransactionSaveResult(false, "Unsupported payment method.", null);
                }

                if (!request.CounterChartOfAccountId.HasValue || request.CounterChartOfAccountId <= 0)
                {
                    return new BankTransactionSaveResult(false, "Pay-to account is required.", null);
                }

                if (request.PaymentMethod != PaymentMethod.CashWithdrawal
                    && !await IsValidPayToCoaAsync(
                        companyId,
                        request.CounterChartOfAccountId.Value,
                        request.CustomerId,
                        request.VendorId,
                        cancellationToken))
                {
                    return new BankTransactionSaveResult(false, "Select a valid pay-to account from Chart of Accounts.", null);
                }

                if (request.PaymentMethod == PaymentMethod.Cheque
                    && await IsCashInHandCoaAsync(companyId, request.CounterChartOfAccountId.Value, cancellationToken)
                    && !await IsValidBankCoaAsync(companyId, request.ChartOfAccountId, cancellationToken))
                {
                    return new BankTransactionSaveResult(
                        false,
                        "Cash withdrawal by cheque requires a bank pay-from account.",
                        null);
                }

                if (request.ChartOfAccountId == request.CounterChartOfAccountId)
                {
                    return new BankTransactionSaveResult(false, "Pay-from and pay-to accounts must be different.", null);
                }

                if (!string.IsNullOrWhiteSpace(request.PartyName)
                    && (request.PartyName.Contains("Sales Tax", StringComparison.OrdinalIgnoreCase)
                        || request.PartyName.Contains("Used Tax", StringComparison.OrdinalIgnoreCase)))
                {
                    var counterAccount = await _unitOfWork.Repository<ChartOfAccount>()
                        .Query()
                        .Where(a => a.Id == request.CounterChartOfAccountId!.Value && a.CompanyId == companyId)
                        .Select(a => a.AccountNumber)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (string.Equals(counterAccount, AccountsPayable, StringComparison.OrdinalIgnoreCase))
                    {
                        return new BankTransactionSaveResult(
                            false,
                            TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId)
                                ? "Sales tax payments must use Further Tax Payable (25510), Sales Tax Payable 18% (25520), or Sales Tax Payable (25500) — not Accounts Payable."
                                : "Sales tax payments must use Sales Tax Payable (25500), not Accounts Payable.",
                            null);
                    }
                }

                break;

            case BankTransactionType.Transfer:
                if (!request.TransferToChartOfAccountId.HasValue || request.TransferToChartOfAccountId <= 0)
                {
                    return new BankTransactionSaveResult(false, "Transfer destination account is required.", null);
                }

                if (request.TransferToChartOfAccountId == request.ChartOfAccountId)
                {
                    return new BankTransactionSaveResult(false, "Cannot transfer to the same account.", null);
                }

                if (!await IsValidTransferCoaAsync(companyId, request.ChartOfAccountId, cancellationToken)
                    || !await IsValidTransferCoaAsync(companyId, request.TransferToChartOfAccountId.Value, cancellationToken))
                {
                    return new BankTransactionSaveResult(
                        false,
                        "Transfers are allowed only between Cash in Hand and bank accounts.",
                        null);
                }

                break;

            default:
                return new BankTransactionSaveResult(false, "Unsupported transaction type.", null);
        }

        return new BankTransactionSaveResult(true, null, null);
    }

    private async Task<(IReadOnlyList<CustomerReceipt>? Receipts, string? ErrorMessage)> LoadUndepositedChequeReceiptsAsync(
        int companyId,
        IReadOnlyList<int> receiptIds,
        DateTime transactionDate,
        CancellationToken cancellationToken)
    {
        if (receiptIds is null || receiptIds.Count == 0)
        {
            return (null, "Select at least one cheque to deposit.");
        }

        var distinctIds = receiptIds.Distinct().ToList();
        var receipts = await _unitOfWork.Repository<CustomerReceipt>()
            .Query(asNoTracking: false)
            .Where(r =>
                r.CompanyId == companyId
                && !r.IsDeleted
                && distinctIds.Contains(r.Id)
                && r.PaymentMethod == PaymentMethod.Cheque
                && r.ChequeBankType != ChequeBankType.SameBank
                && r.ClearedAt == null
                && !r.IsDeposited
                && r.Amount > 0m)
            .ToListAsync(cancellationToken);

        if (receipts.Count != distinctIds.Count)
        {
            return (null, "One or more selected cheques are invalid or already deposited.");
        }

        var depositDate = transactionDate.Date;
        foreach (var receipt in receipts)
        {
            if (receipt.ChequeDate.HasValue && receipt.ChequeDate.Value.Date > depositDate)
            {
                var chequeLabel = !string.IsNullOrWhiteSpace(receipt.ChequeNumber)
                    ? receipt.ChequeNumber.Trim()
                    : receipt.ReceiptNumber;
                return (
                    null,
                    $"Cheque #{chequeLabel} cannot be deposited before its cheque date ({receipt.ChequeDate.Value:dd MMM yyyy}).");
            }
        }

        return (receipts, null);
    }

    private async Task<bool> HasPostedReceiptJournalAsync(
        int companyId,
        int receiptId,
        CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<JournalEntry>()
            .Query()
            .AnyAsync(
                j => j.CompanyId == companyId
                     && j.ReferenceType == ReferenceTypes.CustomerReceipt
                     && j.ReferenceId == receiptId
                     && j.Status == JournalStatus.Posted
                     && !j.IsDeleted,
                cancellationToken);

    private async Task MarkChequesDepositedAsync(
        IReadOnlyList<CustomerReceipt> receipts,
        int bankTransactionId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName;

        foreach (var receipt in receipts)
        {
            receipt.IsDeposited = true;
            receipt.DepositedBankTransactionId = bankTransactionId;
            receipt.UpdatedAt = now;
            receipt.UpdatedBy = userName;
            _unitOfWork.Repository<CustomerReceipt>().Update(receipt);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> IsValidBankCoaAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        var lookups = await QueryBankCoaAccountsAsync(companyId, includeCashInHand: false, cancellationToken);
        return lookups.Any(a => a.Id == chartOfAccountId);
    }

    private async Task<bool> IsValidDepositCoaAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        var lookups = await QueryBankCoaAccountsAsync(companyId, includeCashInHand: true, cancellationToken);
        return lookups.Any(a => a.Id == chartOfAccountId);
    }

    private async Task<bool> IsValidTransferCoaAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        var lookups = await QueryBankCoaAccountsAsync(companyId, includeCashInHand: true, cancellationToken);
        return lookups.Any(a => a.Id == chartOfAccountId);
    }

    private async Task<bool> IsValidCashPayFromCoaAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        if (await IsValidBankCoaAsync(companyId, chartOfAccountId, cancellationToken))
        {
            return true;
        }

        return await IsCashInHandCoaAsync(companyId, chartOfAccountId, cancellationToken);
    }

    private async Task<bool> IsCashInHandCoaAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .AnyAsync(a =>
                a.Id == chartOfAccountId
                && a.CompanyId == companyId
                && a.IsActive
                && !a.IsDeleted
                && a.AccountNumber == CashInHand,
                cancellationToken);
    }

    private async Task<bool> IsValidPayToCoaAsync(
        int companyId,
        int chartOfAccountId,
        int? customerId,
        int? vendorId,
        CancellationToken cancellationToken)
    {
        var parties = await GetWriteChequePartyLookupsAsync(cancellationToken);
        return parties.Any(p =>
            p.ChartOfAccountId == chartOfAccountId
            && p.CustomerId == customerId
            && p.VendorId == vendorId);
    }

    private async Task<IReadOnlyList<BankCoaLookupDto>> QueryBankCoaAccountsAsync(
        int companyId,
        bool includeCashInHand,
        CancellationToken cancellationToken)
    {
        var parentId = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.AccountNumber == BankAccountsParent && a.IsActive)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var query = _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a =>
                a.CompanyId == companyId
                && a.IsActive
                && !a.IsDeleted
                && !a.ChildAccounts.Any()
                && a.TypeId == AssetsTypeId
                && a.AccountNumber != KeptAside
                && a.AccountNumber != UndepositedFunds);

        if (parentId.HasValue)
        {
            query = query.Where(a =>
                a.ParentAccountId == parentId.Value
                || (includeCashInHand && a.AccountNumber == CashInHand));
        }
        else if (includeCashInHand)
        {
            query = query.Where(a =>
                a.SubTypeId == CashAndBankSubTypeId
                || a.AccountNumber == CashInHand);
        }
        else
        {
            query = query.Where(a => a.SubTypeId == CashAndBankSubTypeId && a.AccountNumber != CashInHand);
        }

        if (!includeCashInHand)
        {
            query = query.Where(a =>
                a.AccountNumber != CashInHand
                && a.AccountNumber != UndepositedFunds
                && a.AccountNumber != BankAccountsParent);
        }

        var accounts = await query
            .OrderBy(a => a.AccountNumber)
            .Select(a => new { a.Id, a.AccountNumber, a.AccountName })
            .ToListAsync(cancellationToken);

        var result = new List<BankCoaLookupDto>();
        foreach (var account in accounts)
        {
            var balance = await _bankGlPostingService.GetAccountBalanceAsync(companyId, account.Id, cancellationToken);
            result.Add(new BankCoaLookupDto(
                account.Id,
                account.AccountNumber,
                account.AccountName,
                balance));
        }

        return result;
    }

    private async Task<int?> ResolveBankIdForCoaAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        var bankId = await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.CompanyId == companyId && b.ChartOfAccountId == chartOfAccountId && !b.IsDeleted)
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (bankId.HasValue)
        {
            return bankId;
        }

        var account = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.Id == chartOfAccountId && a.CompanyId == companyId)
            .Select(a => new { a.AccountNumber, a.AccountName })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return null;
        }

        var bankAccountNumber = account.AccountNumber.Trim();
        var suffix = 0;
        while (await _unitOfWork.Repository<Bank>()
                   .Query()
                   .AnyAsync(b => b.CompanyId == companyId && b.AccountNumber == bankAccountNumber, cancellationToken))
        {
            suffix++;
            bankAccountNumber = $"{account.AccountNumber.Trim()}-{suffix}";
        }

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "system";
        var bank = new Bank
        {
            CompanyId = companyId,
            BankName = account.AccountName,
            AccountTitle = account.AccountName,
            AccountNumber = bankAccountNumber,
            ChartOfAccountId = chartOfAccountId,
            OpeningBalance = 0m,
            CurrentBalance = 0m,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = userName
        };

        await _unitOfWork.Repository<Bank>().AddAsync(bank, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return bank.Id;
    }

    private bool TryGetCompanyId(out int companyId, out BankTransactionSaveResult? error)
    {
        try
        {
            companyId = _currentCompany.GetRequiredCompanyId();
            error = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            companyId = 0;
            error = new BankTransactionSaveResult(false, ex.Message, null);
            return false;
        }
    }

    private async Task TryAuditAsync(
        string action,
        string entityId,
        string? oldValues,
        string? newValues,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditService.LogAsync(
                ReferenceTypes.BankTransaction,
                entityId,
                action,
                oldValues,
                newValues,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for bank transaction {EntityId}", entityId);
        }
    }

    private async Task<decimal> GetCustomerOutstandingAsync(
        int companyId,
        int customerId,
        CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.Id == customerId && c.CompanyId == companyId)
            .Select(c =>
                c.OpeningBalance
                + c.SalesInvoices
                    .Where(si => si.Status == InvoiceStatus.Posted)
                    .Sum(si => si.InvoiceType == InvoiceType.CreditNote ? -si.NetTotal : si.NetTotal)
                - c.CustomerReceipts
                    .Where(r => r.PaymentMethod != PaymentMethod.Cheque
                                || (r.Status == CustomerReceiptStatus.Cleared && r.ClearedAt != null))
                    .Sum(r => r.Amount)
                + c.WriteChequePayments
                    .Where(bt => bt.TransactionType == BankTransactionType.Withdrawal && !bt.IsDeleted)
                    .Sum(bt => bt.CustomerBalanceEffect))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<decimal> GetVendorOutstandingAsync(
        int companyId,
        int vendorId,
        CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.Id == vendorId && v.CompanyId == companyId)
            .Select(v =>
                v.OpeningBalance
                + v.VendorBills
                    .Where(b => b.Status == BillStatus.Approved)
                    .Sum(b => b.NetAmount)
                - v.VendorPayments.Sum(p => p.Amount)
                - v.WriteChequePayments
                    .Where(bt => bt.TransactionType == BankTransactionType.Withdrawal && !bt.IsDeleted)
                    .Sum(bt => bt.Amount))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<string?> ResolveChequeNumberForWithdrawalAsync(
        int companyId,
        int bankId,
        int chartOfAccountId,
        string? requestedChequeNumber,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedChequeNumber))
        {
            return requestedChequeNumber.Trim();
        }

        var savedNext = await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.Id == bankId && b.CompanyId == companyId)
            .Select(b => b.NextChequeNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(savedNext))
        {
            return savedNext.Trim();
        }

        return await DeriveNextChequeNumberFromHistoryAsync(companyId, chartOfAccountId, cancellationToken);
    }

    private async Task<string?> DeriveNextChequeNumberFromHistoryAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        var lastUsed = await _unitOfWork.Repository<BankTransaction>()
            .Query()
            .Where(bt =>
                bt.CompanyId == companyId
                && bt.ChartOfAccountId == chartOfAccountId
                && bt.TransactionType == BankTransactionType.Withdrawal
                && !bt.IsDeleted
                && bt.ChequeNumber != null
                && bt.ChequeNumber != "")
            .OrderByDescending(bt => bt.Id)
            .Select(bt => bt.ChequeNumber)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(lastUsed)
            ? null
            : ChequeNumberHelper.Increment(lastUsed.Trim());
    }

    private async Task<string?> ResolvePartyNameForSaveAsync(
        int companyId,
        int? customerId,
        int? vendorId,
        int? counterChartOfAccountId,
        string? partyName,
        CancellationToken cancellationToken)
    {
        if (customerId.HasValue)
        {
            var name = await _unitOfWork.Repository<Customer>()
                .Query()
                .Where(c => c.Id == customerId.Value && c.CompanyId == companyId)
                .Select(c => c.BuyerName)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        if (vendorId.HasValue)
        {
            var name = await _unitOfWork.Repository<Vendor>()
                .Query()
                .Where(v => v.Id == vendorId.Value && v.CompanyId == companyId)
                .Select(v => v.VendorName)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(partyName))
        {
            return partyName.Trim();
        }

        if (counterChartOfAccountId.HasValue)
        {
            var accountName = await _unitOfWork.Repository<ChartOfAccount>()
                .Query()
                .Where(a => a.Id == counterChartOfAccountId.Value && a.CompanyId == companyId)
                .Select(a => a.AccountName)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(accountName))
            {
                return accountName.Trim();
            }
        }

        return partyName;
    }

    private static string GetTransactionTypeLabel(BankTransactionType type) =>
        type switch
        {
            BankTransactionType.Deposit => "Make Deposit",
            BankTransactionType.Withdrawal => "Write Cheque",
            BankTransactionType.Transfer => "Transfer",
            _ => type.ToString()
        };

    private static IQueryable<BankTransaction> ApplyOrdering(IQueryable<BankTransaction> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(t => t.ChartOfAccount.AccountNumber) : query.OrderBy(t => t.ChartOfAccount.AccountNumber),
            1 => desc ? query.OrderByDescending(t => t.TransactionDate) : query.OrderBy(t => t.TransactionDate),
            2 => desc ? query.OrderByDescending(t => t.TransactionType) : query.OrderBy(t => t.TransactionType),
            4 => desc ? query.OrderByDescending(t => t.Amount) : query.OrderBy(t => t.Amount),
            _ => query.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id)
        };
    }
}
