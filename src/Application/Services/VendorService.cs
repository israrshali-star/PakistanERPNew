using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Services;

public partial class VendorService : IVendorService
{
    private const decimal DefaultTaxRate = 18m;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;

    public VendorService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
    }

    public async Task<DataTableResponse<VendorListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(v =>
                v.VendorCode.Contains(term)
                || v.VendorName.Contains(term)
                || (v.NTN != null && v.NTN.Contains(term))
                || (v.Email != null && v.Email.Contains(term))
                || (v.Phone != null && v.Phone.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(v => new VendorListItemDto(
                v.Id,
                v.VendorCode,
                v.VendorName,
                v.Province != null ? v.Province.Name : null,
                v.NTN,
                v.Phone,
                v.DefaultSalesTaxRate,
                v.OpeningBalance,
                v.OpeningBalance + v.VendorBills
                    .Where(b => b.Status == BillStatus.Approved)
                    .Sum(b => b.NetAmount)
                    - v.VendorPayments.Sum(p => p.Amount),
                v.IsActive))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<VendorListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<VendorDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.Id == id && v.CompanyId == companyId)
            .Select(v => new VendorDto(
                v.Id,
                v.VendorCode,
                v.VendorName,
                v.OpeningBalance,
                v.OpeningBalance + v.VendorBills
                    .Where(b => b.Status == BillStatus.Approved)
                    .Sum(b => b.NetAmount)
                    - v.VendorPayments.Sum(p => p.Amount),
                v.Address,
                v.ProvinceId,
                v.Province != null ? v.Province.Name : null,
                v.Phone,
                v.Email,
                v.NTN,
                v.DefaultSalesTaxRate,
                v.IsActive,
                v.VendorBills.Any()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<NextVendorCodeDto> GenerateNextVendorCodeAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var prefix = AppConstants.VendorCodePrefix;

        var codes = await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId && v.VendorCode.StartsWith(prefix))
            .Select(v => v.VendorCode)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var code in codes)
        {
            var match = VendorCodeRegex().Match(code);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
            {
                max = Math.Max(max, number);
            }
        }

        return new NextVendorCodeDto($"{prefix}{(max + 1):D4}");
    }

    public async Task<VendorSaveResult> CreateAsync(
        VendorSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateSaveRequestAsync(request, null, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var now = DateTime.UtcNow;

        var entity = new Vendor
        {
            CompanyId = companyId,
            VendorCode = request.VendorCode.Trim(),
            VendorName = request.VendorName.Trim(),
            OpeningBalance = request.OpeningBalance,
            Address = request.Address?.Trim(),
            ProvinceId = request.ProvinceId,
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            NTN = request.NTN?.Trim(),
            DefaultSalesTaxRate = request.DefaultSalesTaxRate,
            IsActive = request.IsActive,
            CreatedAt = now,
            CreatedBy = _currentUser.UserName ?? "system"
        };

        await _unitOfWork.Repository<Vendor>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "Create",
            "Vendors",
            entity.Id.ToString(),
            null,
            JsonSerializer.Serialize(request),
            cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new VendorSaveResult(true, null, dto);
    }

    public async Task<VendorSaveResult> UpdateAsync(
        VendorSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Id.HasValue)
        {
            return new VendorSaveResult(false, "Vendor id is required.", null);
        }

        var validation = await ValidateSaveRequestAsync(request, request.Id.Value, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var entity = await _unitOfWork.Repository<Vendor>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(v => v.Id == request.Id.Value && v.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new VendorSaveResult(false, "Vendor not found.", null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            entity.VendorCode,
            entity.VendorName,
            entity.OpeningBalance,
            entity.ProvinceId,
            entity.DefaultSalesTaxRate,
            entity.IsActive
        });

        entity.VendorCode = request.VendorCode.Trim();
        entity.VendorName = request.VendorName.Trim();
        entity.OpeningBalance = request.OpeningBalance;
        entity.Address = request.Address?.Trim();
        entity.ProvinceId = request.ProvinceId;
        entity.Phone = request.Phone?.Trim();
        entity.Email = request.Email?.Trim();
        entity.NTN = request.NTN?.Trim();
        entity.DefaultSalesTaxRate = request.DefaultSalesTaxRate;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.UserName;

        _unitOfWork.Repository<Vendor>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "Update",
            "Vendors",
            entity.Id.ToString(),
            oldSnapshot,
            JsonSerializer.Serialize(request),
            cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new VendorSaveResult(true, null, dto);
    }

    public async Task<VendorSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var entity = await _unitOfWork.Repository<Vendor>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(v => v.Id == id && v.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new VendorSaveResult(false, "Vendor not found.", null);
        }

        var hasBills = await _unitOfWork.Repository<VendorBill>()
            .Query()
            .AnyAsync(b => b.VendorId == id, cancellationToken);

        if (hasBills)
        {
            return new VendorSaveResult(
                false,
                "Cannot delete this vendor because vendor bills exist.",
                null);
        }

        var hasPayments = await _unitOfWork.Repository<VendorPayment>()
            .Query()
            .AnyAsync(p => p.VendorId == id, cancellationToken);

        if (hasPayments)
        {
            return new VendorSaveResult(
                false,
                "Cannot delete this vendor because vendor payments exist.",
                null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new { entity.VendorCode, entity.VendorName });
        _unitOfWork.Repository<Vendor>().Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync("Delete", "Vendors", id.ToString(), oldSnapshot, null, cancellationToken);
        return new VendorSaveResult(true, "Vendor deleted successfully.", null);
    }

    public async Task<VendorLedgerDto?> GetLedgerAsync(int id, CancellationToken cancellationToken = default)
    {
        var vendor = await GetByIdAsync(id, cancellationToken);
        if (vendor is null)
        {
            return null;
        }

        var entries = await BuildLedgerEntriesAsync(id, null, null, cancellationToken);
        var closing = entries.Count > 0 ? entries[^1].Balance : vendor.OpeningBalance;

        return new VendorLedgerDto(vendor, entries, closing);
    }

    public async Task<VendorStatementDto?> GetStatementAsync(
        int id,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var vendor = await GetByIdAsync(id, cancellationToken);
        if (vendor is null)
        {
            return null;
        }

        var from = fromDate.Date;
        var to = toDate.Date.AddDays(1).AddTicks(-1);
        var opening = await GetBalanceBeforeDateAsync(id, from, cancellationToken);
        var entries = await BuildLedgerEntriesAsync(id, from, to, cancellationToken, opening);
        var closing = entries.Count > 0 ? entries[^1].Balance : opening;

        return new VendorStatementDto(vendor, from, toDate.Date, opening, entries, closing);
    }

    private async Task<IReadOnlyList<VendorLedgerEntryDto>> BuildLedgerEntriesAsync(
        int vendorId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken,
        decimal? startingBalance = null)
    {
        var vendor = await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.Id == vendorId)
            .Select(v => new { v.OpeningBalance })
            .FirstAsync(cancellationToken);

        var balance = startingBalance ?? vendor.OpeningBalance;
        var entries = new List<VendorLedgerEntryDto>();

        if (!fromDate.HasValue && vendor.OpeningBalance != 0m)
        {
            entries.Add(new VendorLedgerEntryDto(
                DateTime.MinValue,
                "OPENING",
                "Opening Balance",
                vendor.OpeningBalance < 0 ? Math.Abs(vendor.OpeningBalance) : 0m,
                vendor.OpeningBalance > 0 ? vendor.OpeningBalance : 0m,
                vendor.OpeningBalance));
            balance = vendor.OpeningBalance;
        }
        else if (fromDate.HasValue)
        {
            balance = startingBalance ?? await GetBalanceBeforeDateAsync(vendorId, fromDate.Value, cancellationToken);
            if (balance != 0m)
            {
                entries.Add(new VendorLedgerEntryDto(
                    fromDate.Value.AddDays(-1),
                    "B/F",
                    "Balance Brought Forward",
                    balance < 0 ? Math.Abs(balance) : 0m,
                    balance > 0 ? balance : 0m,
                    balance));
            }
        }

        var billQuery = _unitOfWork.Repository<VendorBill>()
            .Query()
            .Where(b => b.VendorId == vendorId && b.Status == BillStatus.Approved);

        if (fromDate.HasValue)
        {
            billQuery = billQuery.Where(b => b.BillDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            billQuery = billQuery.Where(b => b.BillDate <= toDate.Value);
        }

        var bills = await billQuery
            .Select(b => new
            {
                b.Id,
                b.BillDate,
                b.BillNumber,
                b.NetAmount
            })
            .ToListAsync(cancellationToken);

        var paymentQuery = _unitOfWork.Repository<VendorPayment>()
            .Query()
            .Where(p => p.VendorId == vendorId);

        if (fromDate.HasValue)
        {
            paymentQuery = paymentQuery.Where(p => p.PaymentDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            paymentQuery = paymentQuery.Where(p => p.PaymentDate <= toDate.Value);
        }

        var payments = await paymentQuery
            .Select(p => new
            {
                p.Id,
                p.PaymentDate,
                p.PaymentNumber,
                p.PaymentMethod,
                p.Amount
            })
            .ToListAsync(cancellationToken);

        var movements = new List<(DateTime Date, int SortKey, string Reference, string Description, decimal Debit, decimal Credit)>();

        foreach (var bill in bills)
        {
            movements.Add((
                bill.BillDate,
                bill.Id,
                bill.BillNumber,
                "Vendor Bill",
                0m,
                bill.NetAmount));
        }

        foreach (var payment in payments)
        {
            movements.Add((
                payment.PaymentDate,
                1_000_000 + payment.Id,
                payment.PaymentNumber,
                $"Vendor Payment ({payment.PaymentMethod})",
                payment.Amount,
                0m));
        }

        foreach (var movement in movements.OrderBy(m => m.Date).ThenBy(m => m.SortKey))
        {
            balance += movement.Credit - movement.Debit;
            entries.Add(new VendorLedgerEntryDto(
                movement.Date,
                movement.Reference,
                movement.Description,
                movement.Debit,
                movement.Credit,
                balance));
        }

        return entries;
    }

    private async Task<decimal> GetBalanceBeforeDateAsync(
        int vendorId,
        DateTime beforeDate,
        CancellationToken cancellationToken)
    {
        var opening = await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.Id == vendorId)
            .Select(v => v.OpeningBalance)
            .FirstAsync(cancellationToken);

        var billTotal = await _unitOfWork.Repository<VendorBill>()
            .Query()
            .Where(b => b.VendorId == vendorId
                        && b.Status == BillStatus.Approved
                        && b.BillDate < beforeDate)
            .SumAsync(b => (decimal?)b.NetAmount, cancellationToken) ?? 0m;

        var paymentTotal = await _unitOfWork.Repository<VendorPayment>()
            .Query()
            .Where(p => p.VendorId == vendorId && p.PaymentDate < beforeDate)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        return opening + billTotal - paymentTotal;
    }

    private async Task<VendorSaveResult> ValidateSaveRequestAsync(
        VendorSaveRequest request,
        int? existingId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.VendorCode))
        {
            return new VendorSaveResult(false, "Vendor code is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.VendorName))
        {
            return new VendorSaveResult(false, "Vendor name is required.", null);
        }

        if (request.DefaultSalesTaxRate < 0 || request.DefaultSalesTaxRate > 100)
        {
            return new VendorSaveResult(false, "Sales tax rate must be between 0 and 100.", null);
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var codeExists = await _unitOfWork.Repository<Vendor>()
            .Query()
            .AnyAsync(
                v => v.CompanyId == companyId
                     && v.VendorCode == request.VendorCode.Trim()
                     && (!existingId.HasValue || v.Id != existingId.Value),
                cancellationToken);

        if (codeExists)
        {
            return new VendorSaveResult(false, "Vendor code already exists for this company.", null);
        }

        return new VendorSaveResult(true, null, null);
    }

    private static IQueryable<Vendor> ApplyOrdering(IQueryable<Vendor> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(v => v.VendorCode) : query.OrderBy(v => v.VendorCode),
            1 => desc ? query.OrderByDescending(v => v.VendorName) : query.OrderBy(v => v.VendorName),
            6 => desc ? query.OrderByDescending(v => v.DefaultSalesTaxRate) : query.OrderBy(v => v.DefaultSalesTaxRate),
            7 => desc ? query.OrderByDescending(v => v.OpeningBalance) : query.OrderBy(v => v.OpeningBalance),
            _ => desc ? query.OrderByDescending(v => v.VendorName) : query.OrderBy(v => v.VendorName)
        };
    }

    [GeneratedRegex(@"^VEND-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex VendorCodeRegex();
}
