using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using System.Text.Json;

namespace PakistanAccountingERP.Application.Services;

public class BankService : IBankService
{
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

        var rows = await query
            .Select(b => new BankListItemDto(
                b.Id,
                b.BankName,
                b.AccountTitle,
                b.AccountNumber,
                b.CurrentBalance,
                b.IsActive,
                b.BankTransactions.Count))
            .ToListAsync(cancellationToken);

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

        var isUsedOnPayments =
            await _unitOfWork.Repository<CustomerReceipt>().Query().AnyAsync(r => r.BankId == id, cancellationToken)
            || await _unitOfWork.Repository<VendorPayment>().Query().AnyAsync(p => p.BankId == id, cancellationToken);

        return new BankDto(
            row.Id,
            row.BankName,
            row.AccountTitle,
            row.AccountNumber,
            row.IBAN,
            row.ChartOfAccountId,
            row.ChartOfAccountLabel,
            row.OpeningBalance,
            row.CurrentBalance,
            row.IsActive,
            row.TransactionCount,
            isUsedOnPayments);
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
