using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;

    public DashboardService(IUnitOfWork unitOfWork, ICurrentCompanyService currentCompany)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var invoiceQuery = _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.Status == InvoiceStatus.Posted);

        var todaySales = await invoiceQuery
            .Where(i => i.InvoiceDate >= today && i.InvoiceDate < today.AddDays(1))
            .SumAsync(i => (decimal?)i.NetTotal, cancellationToken) ?? 0m;

        var monthSales = await invoiceQuery
            .Where(i => i.InvoiceDate >= monthStart && i.InvoiceDate < monthEnd)
            .SumAsync(i => (decimal?)i.NetTotal, cancellationToken) ?? 0m;

        var postedInvoiceTotal = await invoiceQuery
            .SumAsync(i => (decimal?)i.NetTotal, cancellationToken) ?? 0m;

        var customerOpeningBalances = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .SumAsync(c => (decimal?)c.OpeningBalance, cancellationToken) ?? 0m;

        var outstandingReceivables = customerOpeningBalances + postedInvoiceTotal;

        var outstandingPayables = await _unitOfWork.Repository<VendorBill>()
            .Query()
            .Where(b => b.CompanyId == companyId && b.Status == BillStatus.Approved)
            .SumAsync(b => (decimal?)b.NetAmount, cancellationToken) ?? 0m;

        var inventoryValue = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.IsActive)
            .SumAsync(i => (decimal?)(i.CurrentStock * i.PurchaseRate), cancellationToken) ?? 0m;

        return new DashboardSummaryDto(
            todaySales,
            monthSales,
            outstandingReceivables,
            outstandingPayables,
            inventoryValue);
    }

    public async Task<IReadOnlyList<MonthlySalesPointDto>> GetMonthlySalesAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var today = DateTime.Today;
        var startMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-11);
        var endExclusive = new DateTime(today.Year, today.Month, 1).AddMonths(1);

        var salesByMonth = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.CompanyId == companyId
                        && i.Status == InvoiceStatus.Posted
                        && i.InvoiceDate >= startMonth
                        && i.InvoiceDate < endExclusive)
            .GroupBy(i => new { i.InvoiceDate.Year, i.InvoiceDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Amount = g.Sum(i => i.NetTotal)
            })
            .ToListAsync(cancellationToken);

        var lookup = salesByMonth.ToDictionary(x => (x.Year, x.Month), x => x.Amount);
        var points = new List<MonthlySalesPointDto>();

        for (var i = 0; i < 12; i++)
        {
            var month = startMonth.AddMonths(i);
            lookup.TryGetValue((month.Year, month.Month), out var amount);
            points.Add(new MonthlySalesPointDto(month.ToString("MMM yyyy"), amount));
        }

        return points;
    }

    public async Task<IReadOnlyList<TopCustomerBalanceDto>> GetTopCustomersByBalanceAsync(
        int count = 5,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var invoiceTotals = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.Status == InvoiceStatus.Posted)
            .GroupBy(i => i.CustomerId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(i => i.NetTotal) })
            .ToListAsync(cancellationToken);

        var invoiceLookup = invoiceTotals.ToDictionary(x => x.CustomerId, x => x.Total);

        var customers = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .Select(c => new { c.Id, c.BuyerId, c.BuyerName, c.OpeningBalance })
            .ToListAsync(cancellationToken);

        return customers
            .Select(c =>
            {
                invoiceLookup.TryGetValue(c.Id, out var invoiced);
                return new TopCustomerBalanceDto(
                    c.Id,
                    c.BuyerName,
                    c.BuyerId,
                    c.OpeningBalance + invoiced);
            })
            .Where(c => c.Balance > 0)
            .OrderByDescending(c => c.Balance)
            .Take(count)
            .ToList();
    }

    public async Task<IReadOnlyList<LowStockItemDto>> GetLowStockItemsAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId
                        && i.IsActive
                        && i.CurrentStock < i.MinimumStock)
            .OrderBy(i => i.CurrentStock)
            .Select(i => new LowStockItemDto(
                i.Id,
                i.ItemCode,
                i.ItemName,
                i.CurrentStock,
                i.MinimumStock,
                i.UnitOfMeasure.Symbol ?? i.UnitOfMeasure.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecentInvoiceDto>> GetRecentInvoicesAsync(
        int count = 5,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var invoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.CompanyId == companyId)
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.Id)
            .Take(count)
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                CustomerName = i.Customer.BuyerName,
                i.InvoiceDate,
                i.NetTotal,
                i.Status
            })
            .ToListAsync(cancellationToken);

        return invoices
            .Select(i => new RecentInvoiceDto(
                i.Id,
                i.InvoiceNumber,
                i.CustomerName,
                i.InvoiceDate,
                i.NetTotal,
                i.Status.ToString(),
                GetInvoiceStatusBadgeClass(i.Status)))
            .ToList();
    }

    public async Task<DashboardDataDto> GetDashboardDataAsync(CancellationToken cancellationToken = default)
    {
        var summary = await GetSummaryAsync(cancellationToken);
        var monthlySales = await GetMonthlySalesAsync(cancellationToken);
        var topCustomers = await GetTopCustomersByBalanceAsync(cancellationToken: cancellationToken);
        var lowStock = await GetLowStockItemsAsync(cancellationToken);
        var recent = await GetRecentInvoicesAsync(cancellationToken: cancellationToken);

        return new DashboardDataDto(summary, monthlySales, topCustomers, lowStock, recent);
    }

    private static string GetInvoiceStatusBadgeClass(InvoiceStatus status) =>
        status switch
        {
            InvoiceStatus.Posted => "bg-success",
            InvoiceStatus.Cancelled => "bg-danger",
            _ => "bg-secondary"
        };
}
