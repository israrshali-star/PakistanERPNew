using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using System.Text.Json;

namespace PakistanAccountingERP.Application.Services;

public class BankReconciliationService : IBankReconciliationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILogger<BankReconciliationService> _logger;

    public BankReconciliationService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ILogger<BankReconciliationService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<DataTableResponse<BankReconciliationListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<BankReconciliation>()
            .Query()
            .Where(r => r.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(r => r.Bank.BankName.Contains(term));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(r => new BankReconciliationListItemDto(
                r.Id,
                r.Bank.BankName,
                r.StatementDate,
                r.StatementBalance,
                r.BookBalance,
                r.StatementBalance - r.BookBalance,
                r.IsCompleted,
                r.CreatedAt))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<BankReconciliationListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<BankReconciliationPreviewDto?> GetPreviewAsync(
        int bankId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var bank = await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.Id == bankId && b.CompanyId == companyId)
            .Select(b => new { b.Id, b.BankName, b.AccountNumber, b.CurrentBalance })
            .FirstOrDefaultAsync(cancellationToken);

        if (bank is null)
        {
            return null;
        }

        var unreconciled = await _unitOfWork.Repository<BankTransaction>()
            .Query()
            .Where(t => t.BankId == bankId && !t.IsReconciled)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.Id)
            .Select(t => new BankReconciliationUnreconciledDto(
                t.Id,
                t.TransactionDate,
                t.TransactionType.ToString(),
                t.Amount,
                t.Description,
                t.ChequeNumber))
            .ToListAsync(cancellationToken);

        return new BankReconciliationPreviewDto(
            bank.Id,
            bank.BankName,
            bank.AccountNumber,
            bank.CurrentBalance,
            unreconciled.Count,
            unreconciled);
    }

    public async Task<IReadOnlyList<BankReconciliationBankLookupDto>> GetBankLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.CompanyId == companyId && b.IsActive)
            .OrderBy(b => b.BankName)
            .Select(b => new BankReconciliationBankLookupDto(b.Id, b.BankName, b.AccountNumber))
            .ToListAsync(cancellationToken);
    }

    public async Task<BankReconciliationCompleteResult> CompleteAsync(
        BankReconciliationCompleteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.BankId <= 0)
        {
            return new BankReconciliationCompleteResult(false, "Bank account is required.", null);
        }

        if (request.StatementDate == default)
        {
            return new BankReconciliationCompleteResult(false, "Statement date is required.", null);
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var bank = await _unitOfWork.Repository<Bank>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(b => b.Id == request.BankId && b.CompanyId == companyId, cancellationToken);

        if (bank is null)
        {
            return new BankReconciliationCompleteResult(false, "Bank account not found.", null);
        }

        var statementDate = request.StatementDate.Date;

        var txnQuery = _unitOfWork.Repository<BankTransaction>()
            .Query(asNoTracking: false)
            .Where(t => t.BankId == request.BankId && !t.IsReconciled && t.TransactionDate <= statementDate);

        if (request.TransactionIds.Count > 0)
        {
            txnQuery = txnQuery.Where(t => request.TransactionIds.Contains(t.Id));
        }

        var transactions = await txnQuery.ToListAsync(cancellationToken);

        if (transactions.Count == 0)
        {
            return new BankReconciliationCompleteResult(
                false,
                "No unreconciled transactions to reconcile.",
                null);
        }

        var now = DateTime.UtcNow;
        var reconciliation = new BankReconciliation
        {
            CompanyId = companyId,
            BankId = request.BankId,
            StatementDate = statementDate,
            StatementBalance = request.StatementBalance,
            BookBalance = bank.CurrentBalance,
            IsCompleted = true,
            CreatedAt = now,
            CreatedBy = _currentUser.UserName
        };

        foreach (var txn in transactions)
        {
            txn.IsReconciled = true;
            txn.UpdatedAt = now;
            txn.UpdatedBy = _currentUser.UserName;
            _unitOfWork.Repository<BankTransaction>().Update(txn);
        }

        try
        {
            await _unitOfWork.Repository<BankReconciliation>().AddAsync(reconciliation, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to complete bank reconciliation");
            return new BankReconciliationCompleteResult(false, "Could not save reconciliation.", null);
        }

        await TryAuditAsync(
            "Complete",
            reconciliation.Id.ToString(),
            null,
            JsonSerializer.Serialize(request),
            cancellationToken);

        return new BankReconciliationCompleteResult(
            true,
            $"{transactions.Count} transaction(s) reconciled.",
            reconciliation.Id);
    }

    private bool TryGetCompanyId(out int companyId, out BankReconciliationCompleteResult? error)
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
            error = new BankReconciliationCompleteResult(false, ex.Message, null);
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
                ReferenceTypes.BankReconciliation,
                entityId,
                action,
                oldValues,
                newValues,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for bank reconciliation {EntityId}", entityId);
        }
    }

    private static IQueryable<BankReconciliation> ApplyOrdering(
        IQueryable<BankReconciliation> query,
        DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(r => r.Bank.BankName) : query.OrderBy(r => r.Bank.BankName),
            1 => desc ? query.OrderByDescending(r => r.StatementDate) : query.OrderBy(r => r.StatementDate),
            2 => desc ? query.OrderByDescending(r => r.StatementBalance) : query.OrderBy(r => r.StatementBalance),
            _ => query.OrderByDescending(r => r.StatementDate).ThenByDescending(r => r.Id)
        };
    }
}
