using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using System.Text.Json;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class BankService : IBankService
{
    private const int AssetsTypeId = 1;
    private const int CashAndBankSubTypeId = 1;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILogger<BankService> _logger;

    public BankService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ILogger<BankService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<DataTableResponse<BankListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(b =>
                b.BankName.Contains(term)
                || b.AccountTitle.Contains(term)
                || b.AccountNumber.Contains(term)
                || (b.IBAN != null && b.IBAN.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var pageRows = await query
            .Select(b => new
            {
                b.Id,
                b.BankName,
                b.AccountTitle,
                b.AccountNumber,
                b.ChartOfAccountId,
                b.OpeningBalance,
                b.IsActive,
                TransactionCount = b.BankTransactions.Count
            })
            .ToListAsync(cancellationToken);

        var closingBalanceMap = await GetClosingBalanceMapAsync(companyId, cancellationToken);
        var rows = pageRows
            .Select(b => new BankListItemDto(
                b.Id,
                b.BankName,
                b.AccountTitle,
                b.AccountNumber,
                ResolveGlBalance(b.ChartOfAccountId, b.OpeningBalance, closingBalanceMap),
                b.IsActive,
                b.TransactionCount))
            .ToList();

        return new DataTableResponse<BankListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<BankDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var row = await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.Id == id && b.CompanyId == companyId)
            .Select(b => new
            {
                b.Id,
                b.BankName,
                b.AccountTitle,
                b.AccountNumber,
                b.IBAN,
                b.ChartOfAccountId,
                ChartOfAccountLabel = b.ChartOfAccount != null
                    ? b.ChartOfAccount.AccountNumber + " — " + b.ChartOfAccount.AccountName
                    : null,
                b.OpeningBalance,
                b.CurrentBalance,
                b.IsActive,
                TransactionCount = b.BankTransactions.Count
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var closingBalanceMap = await GetClosingBalanceMapAsync(companyId, cancellationToken);
        var glBalance = ResolveGlBalance(row.ChartOfAccountId, row.OpeningBalance, closingBalanceMap);

        var isUsedOnPayments =
            await _unitOfWork.Repository<CustomerReceipt>().Query()
                .AnyAsync(r => r.BankId == id && r.CompanyId == companyId, cancellationToken)
            || await _unitOfWork.Repository<VendorPayment>().Query()
                .AnyAsync(p => p.BankId == id && p.CompanyId == companyId, cancellationToken);

        return new BankDto(
            row.Id,
            row.BankName,
            row.AccountTitle,
            row.AccountNumber,
            row.IBAN,
            row.ChartOfAccountId,
            row.ChartOfAccountLabel,
            row.OpeningBalance,
            glBalance,
            row.IsActive,
            row.TransactionCount,
            isUsedOnPayments);
    }

    public async Task<IReadOnlyList<BankLookupDto>> GetActiveBankLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        await SyncBanksFromCashAndBankAccountsAsync(companyId, cancellationToken);

        var banks = await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b =>
                b.CompanyId == companyId
                && b.IsActive
                && !b.IsDeleted
                && b.ChartOfAccountId.HasValue
                && b.ChartOfAccount != null
                && b.ChartOfAccount.TypeId == AssetsTypeId
                && b.ChartOfAccount.IsActive
                && !b.ChartOfAccount.IsDeleted
                && (b.ChartOfAccount.SubTypeId == CashAndBankSubTypeId
                    || (b.ChartOfAccount.ParentAccount != null
                        && b.ChartOfAccount.ParentAccount.SubTypeId == CashAndBankSubTypeId
                        && b.ChartOfAccount.ParentAccount.TypeId == AssetsTypeId)))
            .OrderBy(b => b.ChartOfAccount!.AccountNumber)
            .ThenBy(b => b.BankName)
            .Select(b => new
            {
                b.Id,
                b.BankName,
                b.OpeningBalance,
                CoaName = b.ChartOfAccount != null ? b.ChartOfAccount.AccountName : b.BankName,
                CoaNumber = b.ChartOfAccount != null ? b.ChartOfAccount.AccountNumber : b.AccountNumber,
                b.ChartOfAccountId
            })
            .ToListAsync(cancellationToken);

        var closingBalanceMap = await GetClosingBalanceMapAsync(companyId, cancellationToken);
        return banks
            .Select(b => new BankLookupDto(
                b.Id,
                b.CoaName,
                b.CoaNumber,
                ResolveGlBalance(b.ChartOfAccountId, b.OpeningBalance, closingBalanceMap)))
            .ToList();
    }

    public async Task<IReadOnlyList<BankChartOfAccountLookupDto>> GetChartOfAccountLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.IsActive && a.TypeId == 1 && a.SubTypeId == 1)
            .OrderBy(a => a.AccountNumber)
            .Select(a => new BankChartOfAccountLookupDto(a.Id, a.AccountNumber, a.AccountName))
            .ToListAsync(cancellationToken);
    }

    public async Task<BankSaveResult> CreateAsync(
        BankSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateSaveRequestAsync(request, null, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var entity = new Bank
        {
            CompanyId = companyId,
            BankName = request.BankName.Trim(),
            AccountTitle = request.AccountTitle.Trim(),
            AccountNumber = request.AccountNumber.Trim(),
            IBAN = request.IBAN?.Trim(),
            ChartOfAccountId = request.ChartOfAccountId,
            OpeningBalance = request.OpeningBalance,
            CurrentBalance = request.OpeningBalance,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.UserName
        };

        try
        {
            await _unitOfWork.Repository<Bank>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create bank account");
            return new BankSaveResult(false, "Could not save bank account. Check account number is unique.", null);
        }

        await TryAuditAsync("Create", entity.Id.ToString(), null, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new BankSaveResult(true, null, dto);
    }

    public async Task<BankSaveResult> UpdateAsync(
        BankSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Id.HasValue)
        {
            return new BankSaveResult(false, "Bank id is required.", null);
        }

        var validation = await ValidateSaveRequestAsync(request, request.Id.Value, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var entity = await _unitOfWork.Repository<Bank>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(b => b.Id == request.Id.Value && b.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new BankSaveResult(false, "Bank account not found.", null);
        }

        var hasTransactions = await _unitOfWork.Repository<BankTransaction>()
            .Query()
            .AnyAsync(t => t.BankId == entity.Id, cancellationToken);

        if (hasTransactions && entity.OpeningBalance != request.OpeningBalance)
        {
            return new BankSaveResult(
                false,
                "Opening balance cannot be changed after bank transactions exist.",
                null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            entity.BankName,
            entity.AccountTitle,
            entity.AccountNumber,
            entity.OpeningBalance,
            entity.IsActive
        });

        entity.BankName = request.BankName.Trim();
        entity.AccountTitle = request.AccountTitle.Trim();
        entity.AccountNumber = request.AccountNumber.Trim();
        entity.IBAN = request.IBAN?.Trim();
        entity.ChartOfAccountId = request.ChartOfAccountId;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.UserName;

        if (!hasTransactions)
        {
            entity.OpeningBalance = request.OpeningBalance;
            entity.CurrentBalance = request.OpeningBalance;
        }

        try
        {
            _unitOfWork.Repository<Bank>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update bank {BankId}", request.Id);
            return new BankSaveResult(false, "Could not update bank account.", null);
        }

        await TryAuditAsync("Update", entity.Id.ToString(), oldSnapshot, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new BankSaveResult(true, null, dto);
    }

    public async Task<BankSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var entity = await _unitOfWork.Repository<Bank>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new BankSaveResult(false, "Bank account not found.", null);
        }

        var hasTransactions = await _unitOfWork.Repository<BankTransaction>()
            .Query()
            .AnyAsync(t => t.BankId == id, cancellationToken);

        if (hasTransactions)
        {
            return new BankSaveResult(false, "Cannot delete a bank account with transactions.", null);
        }

        var usedOnReceipts = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .AnyAsync(r => r.BankId == id, cancellationToken);

        if (usedOnReceipts)
        {
            return new BankSaveResult(false, "Cannot delete a bank account used on customer receipts.", null);
        }

        var usedOnPayments = await _unitOfWork.Repository<VendorPayment>()
            .Query()
            .AnyAsync(p => p.BankId == id, cancellationToken);

        if (usedOnPayments)
        {
            return new BankSaveResult(false, "Cannot delete a bank account used on vendor payments.", null);
        }

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = _currentUser.UserName;

        _unitOfWork.Repository<Bank>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await TryAuditAsync("Delete", id.ToString(), JsonSerializer.Serialize(entity), null, cancellationToken);

        return new BankSaveResult(true, "Bank account deleted.", null);
    }

    private async Task<BankSaveResult> ValidateSaveRequestAsync(
        BankSaveRequest request,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BankName))
        {
            return new BankSaveResult(false, "Bank name is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.AccountTitle))
        {
            return new BankSaveResult(false, "Account title is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.AccountNumber))
        {
            return new BankSaveResult(false, "Account number is required.", null);
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        if (request.ChartOfAccountId.HasValue)
        {
            var coaExists = await _unitOfWork.Repository<ChartOfAccount>()
                .Query()
                .AnyAsync(a => a.Id == request.ChartOfAccountId
                               && a.CompanyId == companyId
                               && a.IsActive
                               && a.TypeId == 1
                               && a.SubTypeId == 1,
                    cancellationToken);

            if (!coaExists)
            {
                return new BankSaveResult(false, "Selected GL account is not valid.", null);
            }
        }

        var duplicate = await _unitOfWork.Repository<Bank>()
            .Query()
            .AnyAsync(b =>
                b.CompanyId == companyId
                && b.AccountNumber == request.AccountNumber.Trim()
                && (!excludeId.HasValue || b.Id != excludeId.Value),
                cancellationToken);

        if (duplicate)
        {
            return new BankSaveResult(false, "Account number already exists for this company.", null);
        }

        return new BankSaveResult(true, null, null);
    }

    private async Task SyncBanksFromCashAndBankAccountsAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var cashAndBankAccounts = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a =>
                a.CompanyId == companyId
                && a.IsActive
                && !a.IsDeleted
                && a.TypeId == AssetsTypeId
                && !a.ChildAccounts.Any()
                && (a.SubTypeId == CashAndBankSubTypeId
                    || (a.ParentAccount != null
                        && a.ParentAccount.SubTypeId == CashAndBankSubTypeId
                        && a.ParentAccount.TypeId == AssetsTypeId)))
            .OrderBy(a => a.AccountNumber)
            .Select(a => new { a.Id, a.AccountNumber, a.AccountName })
            .ToListAsync(cancellationToken);

        if (cashAndBankAccounts.Count == 0)
        {
            return;
        }

        var linkedCoaIds = await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.CompanyId == companyId && b.ChartOfAccountId.HasValue)
            .Select(b => b.ChartOfAccountId!.Value)
            .ToListAsync(cancellationToken);

        var linkedSet = linkedCoaIds.ToHashSet();
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "system";
        var added = false;

        foreach (var account in cashAndBankAccounts)
        {
            if (linkedSet.Contains(account.Id))
            {
                continue;
            }

            var bankAccountNumber = await ResolveUniqueBankAccountNumberAsync(
                companyId,
                account.AccountNumber,
                cancellationToken);

            await _unitOfWork.Repository<Bank>().AddAsync(new Bank
            {
                CompanyId = companyId,
                BankName = account.AccountName,
                AccountTitle = account.AccountName,
                AccountNumber = bankAccountNumber,
                ChartOfAccountId = account.Id,
                OpeningBalance = 0m,
                CurrentBalance = 0m,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = userName
            }, cancellationToken);

            added = true;
        }

        if (added)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<string> ResolveUniqueBankAccountNumberAsync(
        int companyId,
        string preferredNumber,
        CancellationToken cancellationToken)
    {
        var candidate = preferredNumber.Trim();
        if (!await _unitOfWork.Repository<Bank>()
                .Query()
                .AnyAsync(b => b.CompanyId == companyId && b.AccountNumber == candidate, cancellationToken))
        {
            return candidate;
        }

        var suffix = 1;
        while (true)
        {
            candidate = $"{preferredNumber.Trim()}-{suffix}";
            if (!await _unitOfWork.Repository<Bank>()
                    .Query()
                    .AnyAsync(b => b.CompanyId == companyId && b.AccountNumber == candidate, cancellationToken))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private bool TryGetCompanyId(out int companyId, out BankSaveResult? error)
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
            error = new BankSaveResult(false, ex.Message, null);
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
                ReferenceTypes.Bank,
                entityId,
                action,
                oldValues,
                newValues,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for bank {EntityId}", entityId);
        }
    }

    private static decimal ResolveGlBalance(
        int? chartOfAccountId,
        decimal openingBalance,
        IReadOnlyDictionary<int, decimal> closingBalanceMap) =>
        chartOfAccountId.HasValue
            ? Math.Round(closingBalanceMap.GetValueOrDefault(chartOfAccountId.Value, openingBalance), 2)
            : openingBalance;

    private async Task<Dictionary<int, decimal>> GetClosingBalanceMapAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var openingBalances = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId)
            .Select(a => new
            {
                a.Id,
                a.AccountNumber,
                a.TypeId,
                a.SubTypeId,
                a.OpeningBalance,
                ParentAccountNumber = a.ParentAccount != null ? a.ParentAccount.AccountNumber : null,
                ParentTypeId = a.ParentAccount != null ? a.ParentAccount.TypeId : (int?)null,
                ParentSubTypeId = a.ParentAccount != null ? a.ParentAccount.SubTypeId : (int?)null,
                IsLinkedToBank = a.Banks.Any(b => !b.IsDeleted)
            })
            .ToListAsync(cancellationToken);

        var journalTotals = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l => l.JournalEntry.CompanyId == companyId
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

        var journalLookup = journalTotals.ToDictionary(x => x.AccountId);
        return openingBalances.ToDictionary(
            x => x.Id,
            x =>
            {
                var journal = journalLookup.GetValueOrDefault(x.Id);
                var debit = journal?.Debit ?? 0m;
                var credit = journal?.Credit ?? 0m;
                if (BankLedgerBalance.UsesDebitMinusCreditLedger(
                        x.TypeId,
                        x.SubTypeId,
                        x.IsLinkedToBank,
                        x.ParentTypeId,
                        x.ParentSubTypeId,
                        x.AccountNumber,
                        x.ParentAccountNumber))
                {
                    return BankLedgerBalance.ComputeClosing(x.OpeningBalance, debit, credit);
                }

                return GlAccountBalance.ComputeNet(
                    x.OpeningBalance,
                    debit,
                    credit,
                    x.TypeId,
                    x.AccountNumber);
            });
    }

    private static IQueryable<Bank> ApplyOrdering(IQueryable<Bank> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(b => b.BankName) : query.OrderBy(b => b.BankName),
            1 => desc ? query.OrderByDescending(b => b.AccountTitle) : query.OrderBy(b => b.AccountTitle),
            2 => desc ? query.OrderByDescending(b => b.AccountNumber) : query.OrderBy(b => b.AccountNumber),
            3 => desc ? query.OrderByDescending(b => b.CurrentBalance) : query.OrderBy(b => b.CurrentBalance),
            4 => desc ? query.OrderByDescending(b => b.IsActive) : query.OrderBy(b => b.IsActive),
            _ => query.OrderBy(b => b.BankName)
        };
    }
}
