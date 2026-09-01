using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class SalesReportService : ISalesReportService
{
    private const decimal MinimumVisibleBalance = 1000.00m;

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
            .SingleOrDefaultAsync(cancellationToken);

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

    public async Task<CustomerBalanceReportDto> GetCustomerBalancesAsync(
        SalesReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var (companyId, _, _) = ValidateDateRange(request);
        var fromDate = request.FromDate.Date;
        var toDate = request.ToDate.Date;

        var customersQuery = _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId);

        if (request.CustomerId.HasValue)
        {
            customersQuery = customersQuery.Where(c => c.Id == request.CustomerId.Value);
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

        var customers = await customersQuery
            .Select(c => new { c.Id, c.BuyerId, c.BuyerName, c.OpeningBalance })
            .ToListAsync(cancellationToken);

        var invoiceQuery = _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(si => si.CompanyId == companyId && si.Status == InvoiceStatus.Posted);

        if (request.CustomerId.HasValue)
        {
            invoiceQuery = invoiceQuery.Where(si => si.CustomerId == request.CustomerId.Value);
        }

        var invoices = await invoiceQuery
            .Select(si => new { si.CustomerId, si.InvoiceDate, si.InvoiceType, si.NetTotal })
            .ToListAsync(cancellationToken);

        var receiptQuery = _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => r.CompanyId == companyId);

        if (request.CustomerId.HasValue)
        {
            receiptQuery = receiptQuery.Where(r => r.CustomerId == request.CustomerId.Value);
        }

        var receipts = await receiptQuery
            .Select(r => new
            {
                r.CustomerId,
                r.ReceiptDate,
                r.PaymentMethod,
                r.Status,
                r.ClearedAt,
                r.Amount
            })
            .ToListAsync(cancellationToken);

        var bankQuery = _unitOfWork.Repository<BankTransaction>()
            .Query()
            .Where(bt =>
                bt.CompanyId == companyId
                && bt.CustomerId != null
                && bt.TransactionType == BankTransactionType.Withdrawal
                && bt.JournalEntryId != null);

        if (request.CustomerId.HasValue)
        {
            bankQuery = bankQuery.Where(bt => bt.CustomerId == request.CustomerId.Value);
        }

        var bankMovements = await bankQuery
            .Select(bt => new
            {
                CustomerId = bt.CustomerId!.Value,
                bt.TransactionDate,
                bt.CustomerBalanceEffect
            })
            .ToListAsync(cancellationToken);

        var periodByCustomer = customers.ToDictionary(
            c => c.Id,
            _ => new CustomerPeriodTotals());

        foreach (var invoice in invoices)
        {
            if (!periodByCustomer.TryGetValue(invoice.CustomerId, out var totals))
            {
                continue;
            }

            var net = invoice.InvoiceType == InvoiceType.CreditNote
                ? -invoice.NetTotal
                : invoice.NetTotal;
            ApplyDatedNet(totals, invoice.InvoiceDate.Date, fromDate, toDate, net);
        }

        foreach (var receipt in receipts)
        {
            if (!CustomerReceiptBalanceRules.AffectsCustomerBalance(
                    receipt.PaymentMethod,
                    receipt.Status,
                    receipt.ClearedAt)
                || !periodByCustomer.TryGetValue(receipt.CustomerId, out var totals))
            {
                continue;
            }

            ApplyDatedNet(totals, receipt.ReceiptDate.Date, fromDate, toDate, -receipt.Amount);
        }

        foreach (var movement in bankMovements)
        {
            if (!periodByCustomer.TryGetValue(movement.CustomerId, out var totals))
            {
                continue;
            }

            ApplyDatedNet(totals, movement.TransactionDate.Date, fromDate, toDate, movement.CustomerBalanceEffect);
        }

        var lines = customers
            .Select(customer =>
            {
                var totals = periodByCustomer[customer.Id];
                var openingNet = customer.OpeningBalance + totals.BeforeFromNet;
                var periodDebit = RoundMoney(totals.PeriodDebit);
                var periodCredit = RoundMoney(totals.PeriodCredit);
                var closingNet = openingNet + periodDebit - periodCredit;
                var (openingDebit, openingCredit) = SplitBalance(openingNet);
                var (closingDebit, closingCredit) = SplitBalance(closingNet);

                return new CustomerBalanceLineDto(
                    customer.Id,
                    customer.BuyerId,
                    customer.BuyerName,
                    openingDebit,
                    openingCredit,
                    periodDebit,
                    periodCredit,
                    closingDebit,
                    closingCredit);
            })
            .Where(line => line.ClosingDebit > MinimumVisibleBalance
                           || line.ClosingCredit > MinimumVisibleBalance)
            .OrderBy(line => line.CustomerName)
            .ThenBy(line => line.CustomerCode)
            .ToList();

        return new CustomerBalanceReportDto(
            fromDate,
            toDate,
            request.CustomerId,
            customerLabel,
            lines.Count,
            lines.Sum(l => l.OpeningDebit),
            lines.Sum(l => l.OpeningCredit),
            lines.Sum(l => l.PeriodDebit),
            lines.Sum(l => l.PeriodCredit),
            lines.Sum(l => l.ClosingDebit),
            lines.Sum(l => l.ClosingCredit),
            lines);
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

    private static void ApplyDatedNet(
        CustomerPeriodTotals totals,
        DateTime movementDate,
        DateTime fromDate,
        DateTime toDate,
        decimal net)
    {
        if (movementDate < fromDate)
        {
            totals.BeforeFromNet += net;
            return;
        }

        if (movementDate > toDate)
        {
            return;
        }

        if (net > 0m)
        {
            totals.PeriodDebit += net;
        }
        else if (net < 0m)
        {
            totals.PeriodCredit += Math.Abs(net);
        }
    }

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static (decimal Debit, decimal Credit) SplitBalance(decimal net)
    {
        var rounded = RoundMoney(net);
        if (rounded == 0m)
        {
            return (0m, 0m);
        }

        return rounded > 0m ? (rounded, 0m) : (0m, Math.Abs(rounded));
    }

    private sealed class CustomerPeriodTotals
    {
        public decimal BeforeFromNet { get; set; }
        public decimal PeriodDebit { get; set; }
        public decimal PeriodCredit { get; set; }
    }
}
