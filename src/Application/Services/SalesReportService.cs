using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class SalesReportService : ISalesReportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;

    public SalesReportService(IUnitOfWork unitOfWork, ICurrentCompanyService currentCompany)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
    }

    public async Task<SalesRegisterReportDto> GetSalesRegisterAsync(
        SalesReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var (companyId, from, to) = ValidateDateRange(request);
        var query = BuildInvoiceQuery(companyId, from, to, request);

        if (request.CustomerId.HasValue)
        {
            query = query.Where(i => i.CustomerId == request.CustomerId.Value);
        }

        string? customerLabel = null;
        if (request.CustomerId.HasValue)
        {
            customerLabel = await _unitOfWork.Repository<Customer>()
                .Query()
                .Where(c => c.Id == request.CustomerId.Value && c.CompanyId == companyId)
                .Select(c => c.BuyerId + " — " + c.BuyerName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var invoices = await query
            .OrderBy(i => i.InvoiceDate)
            .ThenBy(i => i.InvoiceNumber)
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.InvoiceDate,
                i.CustomerId,
                i.InvoiceType,
                i.Status,
                i.SubTotal,
                i.DiscountAmount,
                i.TaxAmount,
                i.NetTotal,
                i.FbrInvoiceNumber
            })
            .ToListAsync(cancellationToken);

        var customerIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var customerNames = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.BuyerName })
            .ToListAsync(cancellationToken);
        var customerLookup = customerNames.ToDictionary(c => c.Id, c => c.BuyerName);

        var invoiceLines = invoices
            .Select(i => new SalesRegisterLineDto(
                i.Id,
                i.InvoiceNumber,
                i.InvoiceDate,
                customerLookup.GetValueOrDefault(i.CustomerId, "—"),
                i.InvoiceType.ToString(),
                i.Status.ToString(),
                i.SubTotal,
                i.DiscountAmount,
                i.TaxAmount,
                i.NetTotal,
                i.FbrInvoiceNumber))
            .ToList();

        return new SalesRegisterReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            request.CustomerId,
            customerLabel,
            invoiceLines.Count,
            invoiceLines.Sum(l => l.SubTotal),
            invoiceLines.Sum(l => l.DiscountAmount),
            invoiceLines.Sum(l => l.TaxAmount),
            invoiceLines.Sum(l => l.NetTotal),
            invoiceLines);
    }

    public async Task<SalesByCustomerReportDto> GetSalesByCustomerAsync(
        SalesReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var (companyId, from, to) = ValidateDateRange(request);
        var query = BuildInvoiceQuery(companyId, from, to, request);

        if (request.CustomerId.HasValue)
        {
            query = query.Where(i => i.CustomerId == request.CustomerId.Value);
        }

        var grouped = await query
            .GroupBy(i => i.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                InvoiceCount = g.Count(),
                SubTotal = g.Sum(i => i.SubTotal),
                DiscountAmount = g.Sum(i => i.DiscountAmount),
                TaxAmount = g.Sum(i => i.TaxAmount),
                NetTotal = g.Sum(i => i.NetTotal)
            })
            .ToListAsync(cancellationToken);

        var customerIds = grouped.Select(g => g.CustomerId).Distinct().ToList();
        var customers = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.BuyerId, c.BuyerName })
            .ToListAsync(cancellationToken);
        var customerLookup = customers.ToDictionary(c => c.Id);

        var groupedLines = grouped
            .Select(g =>
            {
                customerLookup.TryGetValue(g.CustomerId, out var customer);
                return new SalesByCustomerLineDto(
                    g.CustomerId,
                    customer?.BuyerId ?? "—",
                    customer?.BuyerName ?? "—",
                    g.InvoiceCount,
                    g.SubTotal,
                    g.DiscountAmount,
                    g.TaxAmount,
                    g.NetTotal);
            })
            .OrderBy(l => l.CustomerName)
            .ToList();

        return new SalesByCustomerReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            groupedLines.Count,
            groupedLines.Sum(l => l.SubTotal),
            groupedLines.Sum(l => l.DiscountAmount),
            groupedLines.Sum(l => l.TaxAmount),
            groupedLines.Sum(l => l.NetTotal),
            groupedLines);
    }

    public async Task<SalesTaxSummaryReportDto> GetSalesTaxSummaryAsync(
        SalesReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var (companyId, from, to) = ValidateDateRange(request);
        var query = BuildInvoiceQuery(companyId, from, to, request);

        if (request.CustomerId.HasValue)
        {
            query = query.Where(i => i.CustomerId == request.CustomerId.Value);
        }

        var summary = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                SubTotal = g.Sum(i => i.SubTotal),
                Discount = g.Sum(i => i.DiscountAmount),
                Tax = g.Sum(i => i.TaxAmount),
                FurtherTax = g.Sum(i => i.FurtherTax),
                Fed = g.Sum(i => i.FED),
                ExtraTax = g.Sum(i => i.ExtraTax),
                WithholdingTax = g.Sum(i => i.WithholdingTax),
                Net = g.Sum(i => i.NetTotal)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new SalesTaxSummaryReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            summary?.Count ?? 0,
            summary?.SubTotal ?? 0m,
            summary?.Discount ?? 0m,
            summary?.Tax ?? 0m,
            summary?.FurtherTax ?? 0m,
            summary?.Fed ?? 0m,
            summary?.ExtraTax ?? 0m,
            summary?.WithholdingTax ?? 0m,
            summary?.Net ?? 0m);
    }

    public async Task<IReadOnlyList<SalesReportCustomerLookupDto>> GetCustomerLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .OrderBy(c => c.BuyerName)
            .Select(c => new SalesReportCustomerLookupDto(c.Id, c.BuyerId, c.BuyerName))
            .ToListAsync(cancellationToken);
    }

    private (int CompanyId, DateTime From, DateTime To) ValidateDateRange(SalesReportRequest request)
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

    private IQueryable<SalesInvoice> BuildInvoiceQuery(
        int companyId,
        DateTime from,
        DateTime to,
        SalesReportRequest request)
    {
        var query = _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.CompanyId == companyId
                        && i.InvoiceDate >= from
                        && i.InvoiceDate <= to);

        if (request.PostedOnly)
        {
            query = query.Where(i => i.Status == InvoiceStatus.Posted);
        }
        else
        {
            query = query.Where(i => i.Status != InvoiceStatus.Cancelled);
        }

        return query;
    }
}
