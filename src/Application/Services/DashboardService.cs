using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;

namespace PakistanAccountingERP.Application.Services;

public class DashboardService : IDashboardService
{
    private const int AssetTypeId = 1;
    private const int CashAndBankSubTypeId = 1;
    private const int LiabilityTypeId = 2;
    private const int RevenueTypeId = 4;
    private const int CogsTypeId = 5;
    private const int ExpenseTypeId = 6;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICompanyService _companyService;

    public DashboardService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICompanyService companyService)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _companyService = companyService;
    }

    private IQueryable<CustomerBalanceRow> CustomerBalanceQuery(int companyId) =>
        _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .Select(c => new CustomerBalanceRow(
                c.Id,
                c.BuyerId,
                c.BuyerName,
                c.OpeningBalance
                    + c.SalesInvoices
                        .Where(si => si.Status == InvoiceStatus.Posted)
                        .Sum(si => si.InvoiceType == InvoiceType.CreditNote ? -si.NetTotal : si.NetTotal)
                    - c.CustomerReceipts
                        .Where(r => r.Status != CustomerReceiptStatus.Returned
                                    && (r.PaymentMethod != PaymentMethod.Cheque
                                        || (r.Status == CustomerReceiptStatus.Cleared && r.ClearedAt != null)))
                        .Sum(r => r.Amount)
                    + c.WriteChequePayments
                        .Where(bt => bt.TransactionType == BankTransactionType.Withdrawal && !bt.IsDeleted)
                        .Sum(bt => bt.CustomerBalanceEffect)));

    private sealed record CustomerBalanceRow(int Id, string BuyerId, string BuyerName, decimal Balance);

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

        var arGlBalance = await GetGlAccountBalanceAsync(
            companyId,
            AccountsReceivable,
            cancellationToken);

        var outstandingReceivables = GlBalanceDisplay.NormalizeNetForDisplay(
            arGlBalance,
            AssetTypeId,
            AccountsReceivable);

        var outstandingPayables = await GetApDashboardClosingBalanceAsync(companyId, cancellationToken);

        var inventoryValue = await GetGlAccountBalanceAsync(
            companyId,
            InventoryAsset,
            cancellationToken);

        var bankClosingBalances = await GetBankClosingBalancesAsync(cancellationToken);
        var cashAndBankBalance = bankClosingBalances.Sum(b => b.ClosingBalance);

        return new DashboardSummaryDto(
            todaySales,
            monthSales,
            outstandingReceivables,
            outstandingPayables,
            inventoryValue,
            cashAndBankBalance);
    }

    public async Task<IReadOnlyList<DailySalesPointDto>> GetDailySalesAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var today = DateTime.Today;
        var start = today.AddDays(-29);
        var endExclusive = today.AddDays(1);

        var lines = await _unitOfWork.Repository<SalesInvoiceLine>()
            .Query()
            .Where(l => l.SalesInvoice.CompanyId == companyId
                        && l.SalesInvoice.Status == InvoiceStatus.Posted
                        && l.SalesInvoice.InvoiceDate >= start
                        && l.SalesInvoice.InvoiceDate < endExclusive)
            .Select(l => new
            {
                l.SalesInvoice.InvoiceDate,
                l.SalesInvoice.InvoiceType,
                l.Cartons
            })
            .ToListAsync(cancellationToken);

        var cartonsByDay = lines
            .GroupBy(l => l.InvoiceDate.Date)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.InvoiceType == InvoiceType.CreditNote ? -x.Cartons : x.Cartons));

        var points = new List<DailySalesPointDto>();
        for (var day = start; day <= today; day = day.AddDays(1))
        {
            cartonsByDay.TryGetValue(day, out var cartons);
            points.Add(new DailySalesPointDto(day.ToString("dd MMM"), day, cartons));
        }

        return points;
    }

    public async Task<IReadOnlyList<MonthlyProfitLossPointDto>> GetMonthlyProfitLossAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var today = DateTime.Today;
        var startMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-11);
        var endExclusive = new DateTime(today.Year, today.Month, 1).AddMonths(1);

        var plAccounts = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId
                        && a.IsActive
                        && (a.TypeId == RevenueTypeId || a.TypeId == CogsTypeId || a.TypeId == ExpenseTypeId))
            .Select(a => new { a.Id, a.TypeId })
            .ToListAsync(cancellationToken);

        if (plAccounts.Count == 0)
        {
            return BuildEmptyMonthlyProfitLossPoints(startMonth);
        }

        var accountIds = plAccounts.Select(a => a.Id).ToList();
        var typeByAccountId = plAccounts.ToDictionary(a => a.Id, a => a.TypeId!.Value);

        var journalLines = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l => accountIds.Contains(l.ChartOfAccountId)
                        && l.JournalEntry.CompanyId == companyId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && l.JournalEntry.EntryDate >= startMonth
                        && l.JournalEntry.EntryDate < endExclusive)
            .Select(l => new
            {
                l.ChartOfAccountId,
                l.Debit,
                l.Credit,
                l.JournalEntry.EntryDate
            })
            .ToListAsync(cancellationToken);

        var totalsByMonth = new Dictionary<(int Year, int Month), (decimal Revenue, decimal Expenses)>();

        foreach (var line in journalLines)
        {
            if (!typeByAccountId.TryGetValue(line.ChartOfAccountId, out var typeId))
            {
                continue;
            }

            var key = (line.EntryDate.Year, line.EntryDate.Month);
            totalsByMonth.TryGetValue(key, out var totals);

            switch (typeId)
            {
                case RevenueTypeId:
                    totals.Revenue += line.Credit - line.Debit;
                    break;
                case CogsTypeId:
                case ExpenseTypeId:
                    totals.Expenses += line.Debit - line.Credit;
                    break;
            }

            totalsByMonth[key] = totals;
        }

        var points = new List<MonthlyProfitLossPointDto>();
        for (var i = 0; i < 12; i++)
        {
            var month = startMonth.AddMonths(i);
            totalsByMonth.TryGetValue((month.Year, month.Month), out var totals);
            var netProfit = totals.Revenue - totals.Expenses;
            points.Add(new MonthlyProfitLossPointDto(
                month.ToString("MMM yyyy"),
                netProfit,
                totals.Revenue,
                totals.Expenses));
        }

        return points;
    }

    private static IReadOnlyList<MonthlyProfitLossPointDto> BuildEmptyMonthlyProfitLossPoints(DateTime startMonth)
    {
        var points = new List<MonthlyProfitLossPointDto>();
        for (var i = 0; i < 12; i++)
        {
            var month = startMonth.AddMonths(i);
            points.Add(new MonthlyProfitLossPointDto(month.ToString("MMM yyyy"), 0m, 0m, 0m));
        }

        return points;
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

        var rows = await CustomerBalanceQuery(companyId)
            .ToListAsync(cancellationToken);

        const int perSide = 5;

        var debitCustomers = rows
            .Where(c => c.Balance > 0.01m)
            .OrderByDescending(c => c.Balance)
            .Take(perSide)
            .Select(c => new TopCustomerBalanceDto(c.Id, c.BuyerName, c.BuyerId, c.Balance, "Dr"));

        var creditCustomers = rows
            .Where(c => c.Balance < -0.01m)
            .OrderBy(c => c.Balance)
            .Take(perSide)
            .Select(c => new TopCustomerBalanceDto(c.Id, c.BuyerName, c.BuyerId, c.Balance, "Cr"));

        return debitCustomers.Concat(creditCustomers).ToList();
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

    public async Task<IReadOnlyList<CompanyApClosingBalanceDto>> GetApClosingBalancesAsync(
        CancellationToken cancellationToken = default)
    {
        var currentCompanyId = _currentCompany.CompanyId;
        var targetCompanyIds = PurchaseWithholdingTaxLayout.PurchaseWithholdingTaxCompanyIds.ToHashSet();
        var userCompanies = await _companyService.GetUserCompaniesAsync(cancellationToken);

        var rows = new List<CompanyApClosingBalanceDto>();
        foreach (var company in userCompanies
                     .Where(c => targetCompanyIds.Contains(c.Id))
                     .OrderBy(c => c.Id))
        {
            rows.Add(new CompanyApClosingBalanceDto(
                company.Id,
                company.CompanyName,
                await GetApDashboardClosingBalanceAsync(company.Id, cancellationToken),
                currentCompanyId == company.Id));
        }

        return rows;
    }

    public async Task<IReadOnlyList<BankCoaClosingBalanceDto>> GetBankClosingBalancesAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var closingBalanceMap = await GetClosingBalanceMapAsync(companyId, cancellationToken);

        var accounts = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a =>
                a.CompanyId == companyId
                && a.IsActive
                && !a.IsDeleted
                && a.TypeId == AssetTypeId
                && a.SubTypeId == CashAndBankSubTypeId
                && !a.ChildAccounts.Any(c => !c.IsDeleted))
            .OrderBy(a => a.AccountNumber)
            .Select(a => new { a.Id, a.AccountNumber, a.AccountName })
            .ToListAsync(cancellationToken);

        return accounts
            .Select(a => new BankCoaClosingBalanceDto(
                a.AccountNumber,
                a.AccountName,
                Math.Round(closingBalanceMap.GetValueOrDefault(a.Id, 0m), 2)))
            .ToList();
    }

    public async Task<DashboardDataDto> GetDashboardDataAsync(CancellationToken cancellationToken = default)
    {
        var summary = await GetSummaryAsync(cancellationToken);
        var dailySales = await GetDailySalesAsync(cancellationToken);
        var monthlyProfitLoss = await GetMonthlyProfitLossAsync(cancellationToken);
        var monthlySales = await GetMonthlySalesAsync(cancellationToken);
        var topCustomers = await GetTopCustomersByBalanceAsync(cancellationToken: cancellationToken);
        var lowStock = await GetLowStockItemsAsync(cancellationToken);
        var recent = await GetRecentInvoicesAsync(cancellationToken: cancellationToken);
        var apClosingBalances = await GetApClosingBalancesAsync(cancellationToken);
        var bankClosingBalances = await GetBankClosingBalancesAsync(cancellationToken);

        return new DashboardDataDto(
            summary,
            dailySales,
            monthlyProfitLoss,
            monthlySales,
            topCustomers,
            lowStock,
            recent,
            apClosingBalances,
            bankClosingBalances);
    }

    private async Task<decimal> GetApDashboardClosingBalanceAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.AccountNumber == AccountsPayable)
            .Select(a => new { a.Id, a.OpeningBalance, a.TypeId, a.AccountNumber })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return 0m;
        }

        var journalTotals = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l => l.ChartOfAccountId == account.Id
                        && l.JournalEntry.CompanyId == companyId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && !l.JournalEntry.IsDeleted)
            .GroupBy(_ => 1)
            .Select(g => new { Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .FirstOrDefaultAsync(cancellationToken);

        var debits = journalTotals?.Debit ?? 0m;
        var credits = journalTotals?.Credit ?? 0m;

        if (PurchaseWithholdingTaxLayout.SupportsPurchaseWithholdingTax(companyId))
        {
            return PurchaseApBalance.ToSignedDisplay(
                account.OpeningBalance,
                debits,
                credits);
        }

        return Math.Round(
            GlAccountBalance.ComputeNet(
                account.OpeningBalance,
                debits,
                credits,
                account.TypeId,
                account.AccountNumber),
            2);
    }

    private async Task<decimal> GetGlAccountBalanceAsync(
        int companyId,
        string accountNumber,
        CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.AccountNumber == accountNumber)
            .Select(a => new { a.Id, a.OpeningBalance, a.TypeId, a.AccountNumber })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return 0m;
        }

        var journalTotals = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l => l.ChartOfAccountId == account.Id
                        && l.JournalEntry.CompanyId == companyId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && !l.JournalEntry.IsDeleted)
            .GroupBy(_ => 1)
            .Select(g => new { Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .FirstOrDefaultAsync(cancellationToken);

        return Math.Round(
            GlAccountBalance.ComputeNet(
                account.OpeningBalance,
                journalTotals?.Debit ?? 0m,
                journalTotals?.Credit ?? 0m,
                account.TypeId,
                account.AccountNumber),
            2);
    }

    private async Task<Dictionary<int, decimal>> GetClosingBalanceMapAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var openingBalances = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId)
            .Select(a => new
            {
                a.Id,
                a.AccountNumber,
                a.TypeId,
                a.SubTypeId,
                a.OpeningBalance,
                ParentTypeId = a.ParentAccount != null ? a.ParentAccount.TypeId : (int?)null,
                ParentSubTypeId = a.ParentAccount != null ? a.ParentAccount.SubTypeId : (int?)null,
                IsLinkedToBank = a.Banks.Any(b => !b.IsDeleted)
            })
            .ToListAsync(cancellationToken);

        var journalTotals = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l => l.JournalEntry.CompanyId == companyId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && !l.JournalEntry.IsDeleted)
            .GroupBy(l => l.ChartOfAccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit)
            })
            .ToListAsync(cancellationToken);

        var journalLookup = journalTotals.ToDictionary(x => x.AccountId);
        return openingBalances.ToDictionary(
            x => x.Id,
            x =>
            {
                var journal = journalLookup.GetValueOrDefault(x.Id);
                var debit = journal?.Debit ?? 0m;
                var credit = journal?.Credit ?? 0m;
                if (BankLedgerBalance.UsesDebitMinusCreditLedger(
                        x.TypeId,
                        x.SubTypeId,
                        x.IsLinkedToBank,
                        x.ParentTypeId,
                        x.ParentSubTypeId))
                {
                    return BankLedgerBalance.ComputeClosing(x.OpeningBalance, debit, credit);
                }

                return GlAccountBalance.ComputeNet(
                    x.OpeningBalance,
                    debit,
                    credit,
                    x.TypeId,
                    x.AccountNumber);
            });
    }

    private static string GetInvoiceStatusBadgeClass(InvoiceStatus status) =>
        status switch
        {
            InvoiceStatus.Posted => "bg-success",
            InvoiceStatus.Cancelled => "bg-danger",
            _ => "bg-secondary"
        };
}
