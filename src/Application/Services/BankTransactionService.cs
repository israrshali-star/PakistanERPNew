using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using System.Text.Json;

namespace PakistanAccountingERP.Application.Services;

public class BankTransactionService : IBankTransactionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILogger<BankTransactionService> _logger;

    public BankTransactionService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ILogger<BankTransactionService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<DataTableResponse<BankTransactionListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        int? bankId = null,
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

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(t =>
                t.Bank.BankName.Contains(term)
                || (t.Description != null && t.Description.Contains(term))
                || (t.ChequeNumber != null && t.ChequeNumber.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(t => new BankTransactionListItemDto(
                t.Id,
                t.Bank.BankName,
                t.TransactionDate,
                t.TransactionType.ToString(),
                t.TransferToBank != null ? t.TransferToBank.BankName : null,
                t.Amount,
                t.Description,
                t.IsReconciled))
            .ToListAsync(cancellationToken);

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
                t.BankId,
                t.Bank.BankName,
                t.TransactionType,
                t.TransferToBankId,
                t.TransferToBank != null ? t.TransferToBank.BankName : null,
                t.TransactionDate,
                t.ChequeNumber,
                t.ChequeDate,
                t.Amount,
                t.Description,
                t.IsReconciled))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BankTransactionBankLookupDto>> GetBankLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.CompanyId == companyId && b.IsActive)
            .OrderBy(b => b.BankName)
            .Select(b => new BankTransactionBankLookupDto(b.Id, b.BankName, b.AccountNumber, b.CurrentBalance))
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

        if (request.BankId <= 0)
        {
            return new BankTransactionSaveResult(false, "Bank account is required.", null);
        }

        if (request.Amount <= 0)
        {
            return new BankTransactionSaveResult(false, "Amount must be greater than zero.", null);
        }

        if (request.TransactionDate == default)
        {
            return new BankTransactionSaveResult(false, "Transaction date is required.", null);
        }

        if (request.TransactionType == BankTransactionType.Transfer)
        {
            if (!request.TransferToBankId.HasValue || request.TransferToBankId <= 0)
            {
                return new BankTransactionSaveResult(false, "Transfer destination bank is required.", null);
            }

            if (request.TransferToBankId == request.BankId)
            {
                return new BankTransactionSaveResult(false, "Cannot transfer to the same bank account.", null);
            }
        }

        var sourceBank = await _unitOfWork.Repository<Bank>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(b => b.Id == request.BankId && b.CompanyId == companyId && b.IsActive, cancellationToken);

        if (sourceBank is null)
        {
            return new BankTransactionSaveResult(false, "Source bank account not found.", null);
        }

        Bank? destBank = null;
        if (request.TransactionType == BankTransactionType.Transfer)
        {
            destBank = await _unitOfWork.Repository<Bank>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(
                    b => b.Id == request.TransferToBankId!.Value && b.CompanyId == companyId && b.IsActive,
                    cancellationToken);

            if (destBank is null)
            {
                return new BankTransactionSaveResult(false, "Destination bank account not found.", null);
            }
        }

        if (request.TransactionType is BankTransactionType.Withdrawal or BankTransactionType.Transfer)
        {
            if (sourceBank.CurrentBalance < request.Amount)
            {
                return new BankTransactionSaveResult(
                    false,
                    $"Insufficient balance. Available: {sourceBank.CurrentBalance:N2}",
                    null);
            }
        }

        var now = DateTime.UtcNow;
        var entity = new BankTransaction
        {
            CompanyId = companyId,
            BankId = request.BankId,
            TransactionType = request.TransactionType,
            TransferToBankId = request.TransferToBankId,
            TransactionDate = request.TransactionDate.Date,
            ChequeNumber = request.ChequeNumber?.Trim(),
            ChequeDate = request.ChequeDate?.Date,
            Amount = request.Amount,
            Description = request.Description?.Trim(),
            IsReconciled = false,
            CreatedAt = now,
            CreatedBy = _currentUser.UserName
        };

        switch (request.TransactionType)
        {
            case BankTransactionType.Deposit:
                sourceBank.CurrentBalance += request.Amount;
                break;
            case BankTransactionType.Withdrawal:
                sourceBank.CurrentBalance -= request.Amount;
                break;
            case BankTransactionType.Transfer:
                sourceBank.CurrentBalance -= request.Amount;
                destBank!.CurrentBalance += request.Amount;
                destBank.UpdatedAt = now;
                destBank.UpdatedBy = _currentUser.UserName;
                _unitOfWork.Repository<Bank>().Update(destBank);
                break;
        }

        sourceBank.UpdatedAt = now;
        sourceBank.UpdatedBy = _currentUser.UserName;

        try
        {
            await _unitOfWork.Repository<BankTransaction>().AddAsync(entity, cancellationToken);
            _unitOfWork.Repository<Bank>().Update(sourceBank);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create bank transaction");
            return new BankTransactionSaveResult(false, "Could not save bank transaction.", null);
        }

        await TryAuditAsync("Create", entity.Id.ToString(), null, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new BankTransactionSaveResult(true, null, dto);
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

    private static IQueryable<BankTransaction> ApplyOrdering(IQueryable<BankTransaction> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(t => t.Bank.BankName) : query.OrderBy(t => t.Bank.BankName),
            1 => desc ? query.OrderByDescending(t => t.TransactionDate) : query.OrderBy(t => t.TransactionDate),
            2 => desc ? query.OrderByDescending(t => t.TransactionType) : query.OrderBy(t => t.TransactionType),
            4 => desc ? query.OrderByDescending(t => t.Amount) : query.OrderBy(t => t.Amount),
            _ => query.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id)
        };
    }
}
