using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Services;

public partial class VendorPaymentService : IVendorPaymentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly IVendorGlPostingService _vendorGlPosting;
    private readonly ILogger<VendorPaymentService> _logger;

    public VendorPaymentService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        IVendorGlPostingService vendorGlPosting,
        ILogger<VendorPaymentService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _vendorGlPosting = vendorGlPosting;
        _logger = logger;
    }

    public async Task<DataTableResponse<VendorPaymentListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<VendorPayment>()
            .Query()
            .Where(p => p.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(p =>
                p.PaymentNumber.Contains(term)
                || p.Vendor.VendorName.Contains(term)
                || p.Vendor.VendorCode.Contains(term)
                || (p.ChequeNumber != null && p.ChequeNumber.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(p => new VendorPaymentListItemDto(
                p.Id,
                p.PaymentNumber,
                p.Vendor.VendorName,
                p.PaymentDate,
                p.Amount,
                p.PaymentMethod.ToString(),
                p.Bank != null ? p.Bank.BankName : null))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<VendorPaymentListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<VendorPaymentDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<VendorPayment>()
            .Query()
            .Where(p => p.Id == id && p.CompanyId == companyId)
            .Select(p => new VendorPaymentDto(
                p.Id,
                p.PaymentNumber,
                p.VendorId,
                p.Vendor.VendorName,
                p.Vendor.VendorCode,
                p.PaymentDate,
                p.Amount,
                p.PaymentMethod,
                p.BankId,
                p.Bank != null ? p.Bank.BankName : null,
                p.ChequeNumber,
                p.ChequeDate,
                p.Notes))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<NextVendorPaymentNumberDto> GenerateNextPaymentNumberAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var prefix = AppConstants.VendorPaymentNumberPrefix;

        var numbers = await _unitOfWork.Repository<VendorPayment>()
            .Query()
            .Where(p => p.CompanyId == companyId && p.PaymentNumber.StartsWith(prefix))
            .Select(p => p.PaymentNumber)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var number in numbers)
        {
            var match = PaymentNumberRegex().Match(number);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var seq))
            {
                max = Math.Max(max, seq);
            }
        }

        return new NextVendorPaymentNumberDto($"{prefix}{(max + 1):D4}");
    }

    public async Task<IReadOnlyList<VendorPaymentVendorLookupDto>> GetVendorLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId && v.IsActive)
            .OrderBy(v => v.VendorName)
            .Select(v => new VendorPaymentVendorLookupDto(
                v.Id,
                v.VendorCode,
                v.VendorName,
                v.OpeningBalance
                    + v.VendorBills
                        .Where(b => b.Status == BillStatus.Approved)
                        .Sum(b => b.NetAmount)
                    - v.VendorPayments.Sum(p => p.Amount)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VendorPaymentBankLookupDto>> GetBankLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.CompanyId == companyId && b.IsActive)
            .OrderBy(b => b.BankName)
            .Select(b => new VendorPaymentBankLookupDto(b.Id, b.BankName, b.AccountNumber))
            .ToListAsync(cancellationToken);
    }

    public async Task<VendorPaymentSaveResult> CreateAsync(
        VendorPaymentSaveRequest request,
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

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName;

        var entity = new VendorPayment
        {
            CompanyId = companyId,
            PaymentNumber = request.PaymentNumber.Trim(),
            VendorId = request.VendorId,
            PaymentDate = request.PaymentDate.Date,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            BankId = request.BankId,
            ChequeNumber = request.ChequeNumber?.Trim(),
            ChequeDate = request.ChequeDate?.Date,
            Notes = request.Notes?.Trim(),
            CreatedAt = now,
            CreatedBy = userName
        };

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            await _unitOfWork.Repository<VendorPayment>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var glResult = await _vendorGlPosting.PostVendorPaymentAsync(entity, cancellationToken);
            if (!glResult.Success)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return new VendorPaymentSaveResult(false, glResult.Message, null);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to create vendor payment");
            return new VendorPaymentSaveResult(
                false,
                "Could not save payment. Check payment number is unique.",
                null);
        }

        await TryAuditAsync("Create", entity.Id.ToString(), null, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new VendorPaymentSaveResult(true, null, dto);
    }

    public async Task<VendorPaymentSaveResult> UpdateAsync(
        VendorPaymentSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Id.HasValue)
        {
            return new VendorPaymentSaveResult(false, "Payment id is required.", null);
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

        var entity = await _unitOfWork.Repository<VendorPayment>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(p => p.Id == request.Id.Value && p.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new VendorPaymentSaveResult(false, "Payment not found.", null);
        }

        var previousAmount = entity.Amount;
        var previousBankId = entity.BankId;
        var previousPaymentMethod = entity.PaymentMethod;

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            entity.PaymentNumber,
            entity.VendorId,
            entity.PaymentDate,
            entity.Amount,
            entity.PaymentMethod,
            entity.BankId,
            entity.ChequeNumber,
            entity.ChequeDate,
            entity.Notes
        });

        entity.PaymentNumber = request.PaymentNumber.Trim();
        entity.VendorId = request.VendorId;
        entity.PaymentDate = request.PaymentDate.Date;
        entity.Amount = request.Amount;
        entity.PaymentMethod = request.PaymentMethod;
        entity.BankId = request.BankId;
        entity.ChequeNumber = request.ChequeNumber?.Trim();
        entity.ChequeDate = request.ChequeDate?.Date;
        entity.Notes = request.Notes?.Trim();
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.UserName;

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            _unitOfWork.Repository<VendorPayment>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var glResult = await _vendorGlPosting.SyncVendorPaymentAsync(
                entity,
                previousAmount,
                previousBankId,
                previousPaymentMethod,
                cancellationToken);

            if (!glResult.Success)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return new VendorPaymentSaveResult(false, glResult.Message, null);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to update vendor payment {PaymentId}", request.Id);
            return new VendorPaymentSaveResult(
                false,
                "Could not update payment. Check payment number is unique.",
                null);
        }

        await TryAuditAsync("Update", entity.Id.ToString(), oldSnapshot, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new VendorPaymentSaveResult(true, null, dto);
    }

    public async Task<VendorPaymentSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var entity = await _unitOfWork.Repository<VendorPayment>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new VendorPaymentSaveResult(false, "Payment not found.", null);
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var glResult = await _vendorGlPosting.RemoveVendorPaymentAsync(entity.Id, cancellationToken);
        if (!glResult.Success)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return new VendorPaymentSaveResult(false, glResult.Message, null);
        }

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = _currentUser.UserName;

        _unitOfWork.Repository<VendorPayment>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await TryAuditAsync("Delete", id.ToString(), JsonSerializer.Serialize(entity), null, cancellationToken);

        return new VendorPaymentSaveResult(true, "Payment deleted.", null);
    }

    private async Task<VendorPaymentSaveResult> ValidateSaveRequestAsync(
        VendorPaymentSaveRequest request,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PaymentNumber))
        {
            return new VendorPaymentSaveResult(false, "Payment number is required.", null);
        }

        if (request.VendorId <= 0)
        {
            return new VendorPaymentSaveResult(false, "Vendor is required.", null);
        }

        if (request.Amount <= 0)
        {
            return new VendorPaymentSaveResult(false, "Amount must be greater than zero.", null);
        }

        if (request.PaymentDate == default)
        {
            return new VendorPaymentSaveResult(false, "Payment date is required.", null);
        }

        if (request.PaymentMethod == PaymentMethod.Cheque && string.IsNullOrWhiteSpace(request.ChequeNumber))
        {
            return new VendorPaymentSaveResult(false, "Cheque number is required for cheque payments.", null);
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var vendorExists = await _unitOfWork.Repository<Vendor>()
            .Query()
            .AnyAsync(v => v.Id == request.VendorId && v.CompanyId == companyId && v.IsActive, cancellationToken);

        if (!vendorExists)
        {
            return new VendorPaymentSaveResult(false, "Selected vendor is not valid.", null);
        }

        if (request.BankId.HasValue)
        {
            var bankExists = await _unitOfWork.Repository<Bank>()
                .Query()
                .AnyAsync(b => b.Id == request.BankId && b.CompanyId == companyId && b.IsActive, cancellationToken);

            if (!bankExists)
            {
                return new VendorPaymentSaveResult(false, "Selected bank account is not valid.", null);
            }
        }

        var duplicateNumber = await _unitOfWork.Repository<VendorPayment>()
            .Query()
            .AnyAsync(p =>
                p.CompanyId == companyId
                && p.PaymentNumber == request.PaymentNumber.Trim()
                && (!excludeId.HasValue || p.Id != excludeId.Value),
                cancellationToken);

        if (duplicateNumber)
        {
            return new VendorPaymentSaveResult(false, "Payment number already exists.", null);
        }

        return new VendorPaymentSaveResult(true, null, null);
    }

    private bool TryGetCompanyId(out int companyId, out VendorPaymentSaveResult? error)
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
            error = new VendorPaymentSaveResult(false, ex.Message, null);
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
                ReferenceTypes.VendorPayment,
                entityId,
                action,
                oldValues,
                newValues,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for vendor payment {EntityId}", entityId);
        }
    }

    private static IQueryable<VendorPayment> ApplyOrdering(IQueryable<VendorPayment> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(p => p.PaymentNumber) : query.OrderBy(p => p.PaymentNumber),
            1 => desc ? query.OrderByDescending(p => p.Vendor.VendorName) : query.OrderBy(p => p.Vendor.VendorName),
            2 => desc ? query.OrderByDescending(p => p.PaymentDate) : query.OrderBy(p => p.PaymentDate),
            3 => desc ? query.OrderByDescending(p => p.Amount) : query.OrderBy(p => p.Amount),
            4 => desc ? query.OrderByDescending(p => p.PaymentMethod) : query.OrderBy(p => p.PaymentMethod),
            5 => desc ? query.OrderByDescending(p => p.Bank!.BankName) : query.OrderBy(p => p.Bank!.BankName),
            _ => query.OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id)
        };
    }

    [GeneratedRegex(@"^VPAY-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PaymentNumberRegex();
}
