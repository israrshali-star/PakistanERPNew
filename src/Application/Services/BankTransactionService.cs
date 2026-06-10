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
    private readonly ILogger<BankTransactionService> _logger;

    public BankTransactionService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        IBankGlPostingService bankGlPostingService,
        ILogger<BankTransactionService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _bankGlPostingService = bankGlPostingService;
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

    public async Task<IReadOnlyList<BankCoaLookupDto>> GetCounterCoaLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var accounts = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.IsActive && !a.IsDeleted && !a.ChildAccounts.Any())
            .OrderBy(a => a.AccountNumber)
            .Select(a => new { a.Id, a.AccountNumber, a.AccountName })
            .ToListAsync(cancellationToken);

        var result = new List<BankCoaLookupDto>();
        foreach (var account in accounts)
        {
            if (account.AccountNumber is CashInHand or UndepositedFunds or BankAccountsParent)
            {
                continue;
            }

            if (account.AccountNumber.StartsWith("100", StringComparison.Ordinal)
                && account.AccountNumber != CashInHand
                && account.AccountNumber != UndepositedFunds
                && account.AccountNumber.Length > 4)
            {
                continue;
            }

            var balance = await _bankGlPostingService.GetAccountBalanceAsync(companyId, account.Id, cancellationToken);
            result.Add(new BankCoaLookupDto(
                account.Id,
                account.AccountNumber,
                account.AccountName,
                balance));
        }

        return result;
    }

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

        return await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r =>
                r.CompanyId == companyId
                && !r.IsDeleted
                && r.PaymentMethod == PaymentMethod.Cheque
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
                r.ChequeDate))
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
            depositReceipts = await LoadUndepositedChequeReceiptsAsync(
                companyId,
                request.CustomerReceiptIds,
                cancellationToken);
            if (depositReceipts is null)
            {
                return new BankTransactionSaveResult(false, "One or more selected cheques are invalid or already deposited.", null);
            }

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
        var entity = new BankTransaction
        {
            CompanyId = companyId,
            BankId = bankId.Value,
            ChartOfAccountId = request.ChartOfAccountId,
            TransactionType = request.TransactionType,
            TransferToBankId = transferBankId,
            TransferToChartOfAccountId = request.TransferToChartOfAccountId,
            CounterChartOfAccountId = request.CounterChartOfAccountId,
            PartyName = request.PartyName?.Trim(),
            TransactionDate = request.TransactionDate.Date,
            ChequeNumber = request.ChequeNumber?.Trim(),
            ChequeDate = request.ChequeDate?.Date,
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

        if (request.TransactionType == BankTransactionType.Withdrawal
            && !string.IsNullOrWhiteSpace(entity.ChequeNumber))
        {
            await AdvanceNextChequeNumberAsync(
                companyId,
                bankId.Value,
                entity.ChequeNumber,
                cancellationToken);
        }

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new BankTransactionSaveResult(true, null, dto);
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
            .Query()
            .FirstOrDefaultAsync(b => b.Id == bankId.Value && b.CompanyId == companyId, cancellationToken);

        if (bank is null)
        {
            return new BankNextChequeNumberSaveResult(false, "Bank account not found.", null);
        }

        bank.NextChequeNumber = nextChequeNumber;
        bank.UpdatedAt = DateTime.UtcNow;
        bank.UpdatedBy = _currentUser.UserName;

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

    private async Task AdvanceNextChequeNumberAsync(
        int companyId,
        int bankId,
        string usedChequeNumber,
        CancellationToken cancellationToken)
    {
        var bank = await _unitOfWork.Repository<Bank>()
            .Query()
            .FirstOrDefaultAsync(b => b.Id == bankId && b.CompanyId == companyId, cancellationToken);

        if (bank is null)
        {
            return;
        }

        var next = ChequeNumberHelper.Increment(usedChequeNumber.Trim());
        if (string.IsNullOrWhiteSpace(next))
        {
            return;
        }

        bank.NextChequeNumber = next;
        bank.UpdatedAt = DateTime.UtcNow;
        bank.UpdatedBy = _currentUser.UserName;

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Failed to advance next cheque number for bank {BankId}", bankId);
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

                if (!await IsValidBankCoaAsync(companyId, request.ChartOfAccountId, cancellationToken))
                {
                    return new BankTransactionSaveResult(false, "Select a valid bank account from Chart of Accounts.", null);
                }

                break;

            case BankTransactionType.Withdrawal:
                if (!await IsValidBankCoaAsync(companyId, request.ChartOfAccountId, cancellationToken))
                {
                    return new BankTransactionSaveResult(false, "Select a valid bank account from Chart of Accounts.", null);
                }

                if (!request.CounterChartOfAccountId.HasValue || request.CounterChartOfAccountId <= 0)
                {
                    return new BankTransactionSaveResult(false, "Pay-to account is required.", null);
                }

                if (string.IsNullOrWhiteSpace(request.PartyName))
                {
                    return new BankTransactionSaveResult(false, "Payee / party name is required.", null);
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

    private async Task<IReadOnlyList<CustomerReceipt>?> LoadUndepositedChequeReceiptsAsync(
        int companyId,
        IReadOnlyList<int> receiptIds,
        CancellationToken cancellationToken)
    {
        if (receiptIds is null || receiptIds.Count == 0)
        {
            return null;
        }

        var distinctIds = receiptIds.Distinct().ToList();
        var receipts = await _unitOfWork.Repository<CustomerReceipt>()
            .Query(asNoTracking: false)
            .Where(r =>
                r.CompanyId == companyId
                && !r.IsDeleted
                && distinctIds.Contains(r.Id)
                && r.PaymentMethod == PaymentMethod.Cheque
                && !r.IsDeposited
                && r.Amount > 0m)
            .ToListAsync(cancellationToken);

        return receipts.Count == distinctIds.Count ? receipts : null;
    }

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

    private async Task<bool> IsValidTransferCoaAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        var lookups = await QueryBankCoaAccountsAsync(companyId, includeCashInHand: true, cancellationToken);
        return lookups.Any(a => a.Id == chartOfAccountId);
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
                && a.TypeId == AssetsTypeId);

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
