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

        var postedInvoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(si => si.CompanyId == companyId && si.Status == InvoiceStatus.Posted)
            .Select(si => new { si.CustomerId, si.InvoiceType, si.NetTotal })
            .ToListAsync(cancellationToken);

        var invoiceTotalsByCustomer = postedInvoices
            .GroupBy(si => si.CustomerId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.InvoiceType == InvoiceType.CreditNote ? -x.NetTotal : x.NetTotal));

        var receiptTotalsByCustomer = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => r.CompanyId == companyId)
            .GroupBy(r => r.CustomerId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(r => r.Amount) })
            .ToListAsync(cancellationToken);

        var receiptLookup = receiptTotalsByCustomer.ToDictionary(x => x.CustomerId, x => x.Total);

        var customers = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .Select(c => new { c.Id, c.OpeningBalance })
            .ToListAsync(cancellationToken);

        var outstandingReceivables = customers.Sum(c =>
        {
            invoiceTotalsByCustomer.TryGetValue(c.Id, out var invoiced);
            receiptLookup.TryGetValue(c.Id, out var receipts);
            return c.OpeningBalance + invoiced - receipts;
        });

        var approvedBills = await _unitOfWork.Repository<VendorBill>()
            .Query()
            .Where(b => b.CompanyId == companyId && b.Status == BillStatus.Approved)
            .Select(b => new { b.VendorId, b.NetAmount })
            .ToListAsync(cancellationToken);

        var billTotalsByVendor = approvedBills
            .GroupBy(b => b.VendorId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.NetAmount));

        var paymentTotalsByVendor = await _unitOfWork.Repository<VendorPayment>()
            .Query()
            .Where(p => p.CompanyId == companyId)
            .GroupBy(p => p.VendorId)
            .Select(g => new { VendorId = g.Key, Total = g.Sum(p => p.Amount) })
            .ToListAsync(cancellationToken);

        var paymentLookup = paymentTotalsByVendor.ToDictionary(x => x.VendorId, x => x.Total);

        var vendors = await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId && v.IsActive)
            .Select(v => new { v.Id, v.OpeningBalance })
            .ToListAsync(cancellationToken);

        var outstandingPayables = vendors.Sum(v =>
        {
            billTotalsByVendor.TryGetValue(v.Id, out var billed);
            paymentLookup.TryGetValue(v.Id, out var paid);
            return v.OpeningBalance + billed - paid;
        });

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

        var salesByMonth = await _unitOfWork.Repository<SalesInvoiceLine>()
            .Query()
            .Where(l => l.SalesInvoice.CompanyId == companyId
                        && l.SalesInvoice.Status == InvoiceStatus.Posted
                        && l.SalesInvoice.InvoiceDate >= startMonth
                        && l.SalesInvoice.InvoiceDate < endExclusive)
            .GroupBy(l => new { l.SalesInvoice.InvoiceDate.Year, l.SalesInvoice.InvoiceDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Cartons = g.Sum(l => l.SalesInvoice.InvoiceType == InvoiceType.CreditNote
                    ? -l.Cartons
                    : l.Cartons)
            })
            .ToListAsync(cancellationToken);

        var lookup = salesByMonth.ToDictionary(x => (x.Year, x.Month), x => x.Cartons);
        var points = new List<MonthlySalesPointDto>();

        for (var i = 0; i < 12; i++)
        {
            var month = startMonth.AddMonths(i);
            lookup.TryGetValue((month.Year, month.Month), out var cartons);
            points.Add(new MonthlySalesPointDto(month.ToString("MMM yyyy"), cartons));
        }

        return points;
    }

    public async Task<IReadOnlyList<TopCustomerBalanceDto>> GetTopCustomersByBalanceAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var postedInvoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.Status == InvoiceStatus.Posted)
            .Select(i => new { i.CustomerId, i.InvoiceType, i.NetTotal })
            .ToListAsync(cancellationToken);

        var invoiceTotalsByCustomer = postedInvoices
            .GroupBy(i => i.CustomerId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.InvoiceType == InvoiceType.CreditNote ? -x.NetTotal : x.NetTotal));

        var receiptTotalsByCustomer = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => r.CompanyId == companyId)
            .GroupBy(r => r.CustomerId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(r => r.Amount) })
            .ToListAsync(cancellationToken);

        var receiptLookup = receiptTotalsByCustomer.ToDictionary(x => x.CustomerId, x => x.Total);

        var customers = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .Select(c => new { c.Id, c.BuyerId, c.BuyerName, c.OpeningBalance })
            .ToListAsync(cancellationToken);

        return customers
            .Select(c =>
            {
                invoiceTotalsByCustomer.TryGetValue(c.Id, out var invoiced);
                receiptLookup.TryGetValue(c.Id, out var receipts);
                return new TopCustomerBalanceDto(
                    c.Id,
                    c.BuyerName,
                    c.BuyerId,
                    c.OpeningBalance + invoiced - receipts);
            })
            .Where(c => c.Balance != 0)
            .OrderBy(c => c.Balance)
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
                i.CustomerId,
                i.InvoiceDate,
                i.NetTotal,
                i.Status
            })
            .ToListAsync(cancellationToken);

        if (invoices.Count == 0)
        {
            return [];
        }

        var customerIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var customerNames = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.BuyerName })
            .ToListAsync(cancellationToken);

        var customerLookup = customerNames.ToDictionary(c => c.Id, c => c.BuyerName);

        return invoices
            .Select(i => new RecentInvoiceDto(
                i.Id,
                i.InvoiceNumber,
                customerLookup.GetValueOrDefault(i.CustomerId, "—"),
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
