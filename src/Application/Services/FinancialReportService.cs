using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class FinancialReportService : IFinancialReportService
{
    private static readonly JournalAggregate EmptyJournal = new();

    private const int AssetTypeId = 1;
    private const int LiabilityTypeId = 2;
    private const int EquityTypeId = 3;
    private const int RevenueTypeId = 4;
    private const int CogsTypeId = 5;
    private const int ExpenseTypeId = 6;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;

    public FinancialReportService(IUnitOfWork unitOfWork, ICurrentCompanyService currentCompany)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
    }

    public async Task<TrialBalanceReportDto> GetTrialBalanceAsync(
        FinancialReportDateRangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var (companyId, from, to) = ValidateDateRange(request);
        var accounts = await GetAccountsAsync(companyId, cancellationToken);
        var journalByAccount = await GetJournalTotalsByAccountAsync(companyId, cancellationToken);

        var lines = new List<TrialBalanceLineDto>();

        foreach (var account in accounts)
        {
            var journal = journalByAccount.GetValueOrDefault(account.Id) ?? EmptyJournal;
            var beforeFromDebit = journal.BeforeFromDebit(from);
            var beforeFromCredit = journal.BeforeFromCredit(from);
            var openingNet = account.OpeningBalance + beforeFromDebit - beforeFromCredit;
            var periodDebit = journal.PeriodDebit(from, to);
            var periodCredit = journal.PeriodCredit(from, to);
            var closingNet = account.OpeningBalance + journal.UpToToDebit(to) - journal.UpToToCredit(to);

            if (openingNet == 0m && periodDebit == 0m && periodCredit == 0m && closingNet == 0m)
            {
                continue;
            }

            lines.Add(new TrialBalanceLineDto(
                account.Id,
                account.AccountNumber,
                account.AccountName,
                account.TypeName,
                openingNet,
                periodDebit,
                periodCredit,
                closingNet,
                closingNet >= 0m ? closingNet : 0m,
                closingNet < 0m ? -closingNet : 0m));
        }

        return new TrialBalanceReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            lines.Count,
            lines.Sum(l => l.ClosingDebit),
            lines.Sum(l => l.ClosingCredit),
            lines.OrderBy(l => l.AccountNumber).ToList());
    }

    public async Task<ProfitAndLossReportDto> GetProfitAndLossAsync(
        FinancialReportDateRangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var (companyId, from, to) = ValidateDateRange(request);
        var accounts = await GetAccountsAsync(companyId, cancellationToken);
        var journalByAccount = await GetJournalTotalsByAccountAsync(companyId, cancellationToken);

        var plAccounts = accounts
            .Where(a => a.TypeId is RevenueTypeId or CogsTypeId or ExpenseTypeId)
            .OrderBy(a => a.TypeId)
            .ThenBy(a => a.AccountNumber)
            .ToList();

        var lines = new List<ProfitAndLossLineDto>();
        decimal totalRevenue = 0m;
        decimal totalCogs = 0m;
        decimal totalExpenses = 0m;

        foreach (var account in plAccounts)
        {
            var journal = journalByAccount.GetValueOrDefault(account.Id) ?? EmptyJournal;
            var periodDebit = journal.PeriodDebit(from, to);
            var periodCredit = journal.PeriodCredit(from, to);
            var amount = account.TypeId switch
            {
                RevenueTypeId => periodCredit - periodDebit,
                _ => periodDebit - periodCredit
            };

            if (amount == 0m)
            {
                continue;
            }

            var section = account.TypeId switch
            {
                RevenueTypeId => "Revenue",
                CogsTypeId => "Cost of Goods Sold",
                _ => "Expenses"
            };

            lines.Add(new ProfitAndLossLineDto(
                account.Id,
                account.AccountNumber,
                account.AccountName,
                section,
                amount));

            switch (account.TypeId)
            {
                case RevenueTypeId:
                    totalRevenue += amount;
                    break;
                case CogsTypeId:
                    totalCogs += amount;
                    break;
                case ExpenseTypeId:
                    totalExpenses += amount;
                    break;
            }
        }

        var grossProfit = totalRevenue - totalCogs;
        var netProfit = grossProfit - totalExpenses;

        return new ProfitAndLossReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            totalRevenue,
            totalCogs,
            totalExpenses,
            grossProfit,
            netProfit,
            lines);
    }

    public async Task<BalanceSheetReportDto> GetBalanceSheetAsync(
        BalanceSheetReportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.AsOfDate == default)
        {
            throw new InvalidOperationException("As-of date is required.");
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var asOf = request.AsOfDate.Date;
        var accounts = await GetAccountsAsync(companyId, cancellationToken);
        var journalByAccount = await GetJournalTotalsByAccountAsync(companyId, cancellationToken);

        var yearStart = new DateTime(asOf.Year, 1, 1);
        var plRequest = new FinancialReportDateRangeRequest { FromDate = yearStart, ToDate = asOf };
        var netIncomeYtd = (await GetProfitAndLossAsync(plRequest, cancellationToken)).NetProfit;

        var lines = new List<BalanceSheetLineDto>();
        decimal totalAssets = 0m;
        decimal totalLiabilities = 0m;
        decimal totalEquity = 0m;

        foreach (var account in accounts.Where(a => a.TypeId is AssetTypeId or LiabilityTypeId or EquityTypeId))
        {
            var journal = journalByAccount.GetValueOrDefault(account.Id) ?? EmptyJournal;
            var net = account.OpeningBalance + journal.UpToToDebit(asOf) - journal.UpToToCredit(asOf);
            var amount = account.TypeId == AssetTypeId ? net : -net;

            if (amount == 0m)
            {
                continue;
            }

            var section = account.TypeId switch
            {
                AssetTypeId => "Assets",
                LiabilityTypeId => "Liabilities",
                _ => "Equity"
            };

            lines.Add(new BalanceSheetLineDto(
                account.Id,
                account.AccountNumber,
                account.AccountName,
                section,
                amount));

            switch (account.TypeId)
            {
                case AssetTypeId:
                    totalAssets += amount;
                    break;
                case LiabilityTypeId:
                    totalLiabilities += amount;
                    break;
                case EquityTypeId:
                    totalEquity += amount;
                    break;
            }
        }

        if (netIncomeYtd != 0m)
        {
            lines.Add(new BalanceSheetLineDto(
                0,
                "—",
                "Net Income (YTD)",
                "Equity",
                netIncomeYtd));
            totalEquity += netIncomeYtd;
        }

        return new BalanceSheetReportDto(
            asOf,
            totalAssets,
            totalLiabilities,
            totalEquity,
            netIncomeYtd,
            totalLiabilities + totalEquity,
            lines.OrderBy(l => l.Section).ThenBy(l => l.AccountNumber).ToList());
    }

    private (int CompanyId, DateTime From, DateTime To) ValidateDateRange(FinancialReportDateRangeRequest request)
    {
        if (request.FromDate == default || request.ToDate == default)
        {
            throw new InvalidOperationException("From and to dates are required.");
        }

        if (request.FromDate.Date > request.ToDate.Date)
        {
            throw new InvalidOperationException("From date cannot be after to date.");
        }

        return (_currentCompany.GetRequiredCompanyId(), request.FromDate.Date, request.ToDate.Date);
    }

    private async Task<List<AccountRow>> GetAccountsAsync(int companyId, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.IsActive)
            .OrderBy(a => a.AccountNumber)
            .Select(a => new AccountRow(
                a.Id,
                a.AccountNumber,
                a.AccountName,
                a.TypeId,
                a.AccountType != null ? a.AccountType.TypeName : null,
                a.OpeningBalance))
            .ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<int, JournalAggregate>> GetJournalTotalsByAccountAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var entries = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l => l.JournalEntry.CompanyId == companyId
                        && l.JournalEntry.Status == JournalStatus.Posted)
            .Select(l => new
            {
                l.ChartOfAccountId,
                l.Debit,
                l.Credit,
                l.JournalEntry.EntryDate
            })
            .ToListAsync(cancellationToken);

        var lookup = new Dictionary<int, JournalAggregate>();

        foreach (var entry in entries)
        {
            if (!lookup.TryGetValue(entry.ChartOfAccountId, out var aggregate))
            {
                aggregate = new JournalAggregate();
                lookup[entry.ChartOfAccountId] = aggregate;
            }

            aggregate.Add(entry.EntryDate.Date, entry.Debit, entry.Credit);
        }

        return lookup;
    }

    private sealed record AccountRow(
        int Id,
        string AccountNumber,
        string AccountName,
        int? TypeId,
        string? TypeName,
        decimal OpeningBalance);

    private sealed class JournalAggregate
    {
        private readonly List<(DateTime Date, decimal Debit, decimal Credit)> _entries = [];

        public void Add(DateTime date, decimal debit, decimal credit)
        {
            _entries.Add((date, debit, credit));
        }

        public decimal BeforeFromDebit(DateTime from) =>
            _entries.Where(e => e.Date < from).Sum(e => e.Debit);

        public decimal BeforeFromCredit(DateTime from) =>
            _entries.Where(e => e.Date < from).Sum(e => e.Credit);

        public decimal PeriodDebit(DateTime from, DateTime to) =>
            _entries.Where(e => e.Date >= from && e.Date <= to).Sum(e => e.Debit);

        public decimal PeriodCredit(DateTime from, DateTime to) =>
            _entries.Where(e => e.Date >= from && e.Date <= to).Sum(e => e.Credit);

        public decimal UpToToDebit(DateTime to) =>
            _entries.Where(e => e.Date <= to).Sum(e => e.Debit);

        public decimal UpToToCredit(DateTime to) =>
            _entries.Where(e => e.Date <= to).Sum(e => e.Credit);
    }
}
