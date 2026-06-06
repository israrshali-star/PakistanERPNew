using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class PurchaseReportService : IPurchaseReportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;

    public PurchaseReportService(IUnitOfWork unitOfWork, ICurrentCompanyService currentCompany)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
    }

    public async Task<PurchaseRegisterReportDto> GetPurchaseRegisterAsync(
        PurchaseReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var (companyId, from, to) = ValidateDateRange(request);
        var query = BuildBillQuery(companyId, from, to, request);

        if (request.VendorId.HasValue)
        {
            query = query.Where(b => b.VendorId == request.VendorId.Value);
        }

        string? vendorLabel = null;
        if (request.VendorId.HasValue)
        {
            vendorLabel = await _unitOfWork.Repository<Vendor>()
                .Query()
                .Where(v => v.Id == request.VendorId.Value && v.CompanyId == companyId)
                .Select(v => v.VendorCode + " — " + v.VendorName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var bills = await query
            .OrderBy(b => b.BillDate)
            .ThenBy(b => b.BillNumber)
            .Select(b => new PurchaseRegisterLineDto(
                b.Id,
                b.BillNumber,
                b.RefNo,
                b.BillDate,
                b.Vendor.VendorName,
                b.Status.ToString(),
                b.TotalQuantity,
                b.TaxAmount,
                b.NetAmount))
            .ToListAsync(cancellationToken);

        return new PurchaseRegisterReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            request.VendorId,
            vendorLabel,
            bills.Count,
            bills.Sum(l => l.TotalQuantity),
            bills.Sum(l => l.TaxAmount),
            bills.Sum(l => l.NetAmount),
            bills);
    }

    public async Task<PurchaseByVendorReportDto> GetPurchaseByVendorAsync(
        PurchaseReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var (companyId, from, to) = ValidateDateRange(request);
        var query = BuildBillQuery(companyId, from, to, request);

        if (request.VendorId.HasValue)
        {
            query = query.Where(b => b.VendorId == request.VendorId.Value);
        }

        var grouped = await query
            .GroupBy(b => new { b.VendorId, b.Vendor.VendorCode, b.Vendor.VendorName })
            .Select(g => new PurchaseByVendorLineDto(
                g.Key.VendorId,
                g.Key.VendorCode,
                g.Key.VendorName,
                g.Count(),
                g.Sum(b => b.TotalQuantity),
                g.Sum(b => b.TaxAmount),
                g.Sum(b => b.NetAmount)))
            .OrderBy(l => l.VendorName)
            .ToListAsync(cancellationToken);

        return new PurchaseByVendorReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            grouped.Count,
            grouped.Sum(l => l.TotalQuantity),
            grouped.Sum(l => l.TaxAmount),
            grouped.Sum(l => l.NetAmount),
            grouped);
    }

    public async Task<InputTaxSummaryReportDto> GetInputTaxSummaryAsync(
        PurchaseReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var (companyId, from, to) = ValidateDateRange(request);
        var query = BuildBillQuery(companyId, from, to, request);

        if (request.VendorId.HasValue)
        {
            query = query.Where(b => b.VendorId == request.VendorId.Value);
        }

        var summary = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Quantity = g.Sum(b => b.TotalQuantity),
                Tax = g.Sum(b => b.TaxAmount),
                Net = g.Sum(b => b.NetAmount)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new InputTaxSummaryReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            summary?.Count ?? 0,
            summary?.Quantity ?? 0m,
            summary?.Tax ?? 0m,
            summary?.Net ?? 0m);
    }

    public async Task<IReadOnlyList<PurchaseReportVendorLookupDto>> GetVendorLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId && v.IsActive)
            .OrderBy(v => v.VendorName)
            .Select(v => new PurchaseReportVendorLookupDto(v.Id, v.VendorCode, v.VendorName))
            .ToListAsync(cancellationToken);
    }

    private (int CompanyId, DateTime From, DateTime To) ValidateDateRange(PurchaseReportRequest request)
    {
        if (request.FromDate == default || request.ToDate == default)
        {
            throw new InvalidOperationException("From and to dates are required.");
        }

        if (request.FromDate.Date > request.ToDate.Date)
        {
            throw new InvalidOperationException("From date cannot be after to date.");
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var from = request.FromDate.Date;
        var to = request.ToDate.Date.AddDays(1).AddTicks(-1);
        return (companyId, from, to);
    }

    private IQueryable<VendorBill> BuildBillQuery(
        int companyId,
        DateTime from,
        DateTime to,
        PurchaseReportRequest request)
    {
        var query = _unitOfWork.Repository<VendorBill>()
            .Query()
            .Where(b => b.CompanyId == companyId
                        && b.BillDate >= from
                        && b.BillDate <= to);

        if (request.ApprovedOnly)
        {
            query = query.Where(b => b.Status == BillStatus.Approved);
        }
        else
        {
            query = query.Where(b => b.Status != BillStatus.Cancelled);
        }

        return query;
    }
}
