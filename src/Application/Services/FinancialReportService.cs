using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;

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
            var openingNet = GlAccountBalance.ComputeNet(
                account.OpeningBalance,
                beforeFromDebit,
                beforeFromCredit,
                account.TypeId,
                account.AccountNumber);
            var periodDebit = journal.PeriodDebit(from, to);
            var periodCredit = journal.PeriodCredit(from, to);
            var closingNet = GlAccountBalance.ComputeNet(
                account.OpeningBalance,
                journal.UpToToDebit(to),
                journal.UpToToCredit(to),
                account.TypeId,
                account.AccountNumber);
            var displayClosing = GlBalanceDisplay.NormalizeNetForDisplay(closingNet, account.TypeId, account.AccountNumber);
            var displayOpening = GlBalanceDisplay.NormalizeNetForDisplay(openingNet, account.TypeId, account.AccountNumber);
            var (closingDebit, closingCredit) = GlTrialBalanceColumns.SplitClosingBalance(
                closingNet,
                account.TypeId,
                account.AccountNumber,
                companyId);

            if (displayOpening == 0m && periodDebit == 0m && periodCredit == 0m && displayClosing == 0m)
            {
                continue;
            }

            lines.Add(new TrialBalanceLineDto(
                account.Id,
                account.AccountNumber,
                account.AccountName,
                account.TypeName,
                displayOpening,
                periodDebit,
                periodCredit,
                displayClosing,
                closingDebit,
                closingCredit));
        }

        var orderedLines = lines.OrderBy(l => l.AccountNumber).ToList();
        var accountById = accounts.ToDictionary(a => a.Id);
        var rows = BuildTrialBalanceRows(orderedLines, accountById);

        var totalClosingDebit = orderedLines.Sum(l => l.ClosingDebit);
        var totalClosingCredit = orderedLines.Sum(l => l.ClosingCredit);
        var difference = Math.Round(totalClosingDebit - totalClosingCredit, 2);

        return new TrialBalanceReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            orderedLines.Count,
            totalClosingDebit,
            totalClosingCredit,
            Math.Abs(difference) < 0.01m,
            difference,
            orderedLines,
            rows);
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

        var rows = BuildProfitAndLossRows(
            accounts,
            journalByAccount,
            from,
            to,
            totalRevenue,
            totalCogs,
            totalExpenses,
            grossProfit,
            netProfit);

        return new ProfitAndLossReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            totalRevenue,
            totalCogs,
            totalExpenses,
            grossProfit,
            netProfit,
            lines,
            rows);
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

        var netIncome = CalculateCumulativeNetIncome(accounts, journalByAccount, asOf);

        var lines = new List<BalanceSheetLineDto>();
        decimal totalAssets = 0m;
        decimal totalLiabilities = 0m;
        decimal totalEquity = 0m;

        foreach (var account in accounts.Where(a =>
                     a.TypeId is AssetTypeId or LiabilityTypeId or EquityTypeId
                     && !string.Equals(a.AccountNumber, OpeningBalanceEquity, StringComparison.OrdinalIgnoreCase)))
        {
            var journal = journalByAccount.GetValueOrDefault(account.Id) ?? EmptyJournal;
            var net = GlAccountBalance.ComputeNet(
                account.OpeningBalance,
                journal.UpToToDebit(asOf),
                journal.UpToToCredit(asOf),
                account.TypeId,
                account.AccountNumber);
            var amount = GlBalanceDisplay.NormalizeNetForDisplay(net, account.TypeId, account.AccountNumber);

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

        if (netIncome != 0m)
        {
            lines.Add(new BalanceSheetLineDto(
                0,
                "—",
                "Net Income",
                "Equity",
                netIncome));
            totalEquity += netIncome;
        }

        var balanceGap = Math.Round(totalAssets - totalLiabilities - totalEquity, 2);
        if (Math.Abs(balanceGap) >= 0.01m)
        {
            lines.Add(new BalanceSheetLineDto(
                0,
                RetainedEarnings,
                "Retained Earnings",
                "Equity",
                balanceGap));
            totalEquity += balanceGap;
        }

        var orderedLines = lines.OrderBy(l => l.Section).ThenBy(l => l.AccountNumber).ToList();
        var rows = BuildBalanceSheetRows(
            accounts,
            journalByAccount,
            asOf,
            netIncome,
            balanceGap,
            totalAssets,
            totalLiabilities,
            totalEquity,
            totalLiabilities + totalEquity);

        return new BalanceSheetReportDto(
            asOf,
            totalAssets,
            totalLiabilities,
            totalEquity,
            netIncome,
            totalLiabilities + totalEquity,
            Math.Abs(totalAssets - (totalLiabilities + totalEquity)) < 0.01m,
            Math.Round(totalAssets - (totalLiabilities + totalEquity), 2),
            orderedLines,
            rows);
    }

    public async Task<ArAgingSummaryReportDto> GetArAgingSummaryAsync(
        ArAgingReportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.AsOfDate == default)
        {
            throw new InvalidOperationException("As-of date is required.");
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var asOf = request.AsOfDate.Date;

        var customers = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .OrderBy(c => c.BuyerName)
            .Select(c => new { c.Id, c.BuyerId, c.BuyerName, c.OpeningBalance })
            .ToListAsync(cancellationToken);

        var customerIds = customers.Select(c => c.Id).ToList();

        var invoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(si => si.CompanyId == companyId
                         && customerIds.Contains(si.CustomerId)
                         && si.Status == InvoiceStatus.Posted
                         && si.InvoiceDate <= asOf)
            .Select(si => new
            {
                si.CustomerId,
                si.InvoiceDate,
                si.InvoiceType,
                si.NetTotal
            })
            .ToListAsync(cancellationToken);

        var receipts = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => r.CompanyId == companyId
                        && customerIds.Contains(r.CustomerId)
                        && r.ReceiptDate <= asOf)
            .Select(r => new { r.CustomerId, r.Amount })
            .ToListAsync(cancellationToken);

        var invoicesByCustomer = invoices
            .GroupBy(i => i.CustomerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var receiptsByCustomer = receipts
            .GroupBy(r => r.CustomerId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

        var lines = new List<ArAgingLineDto>();

        foreach (var customer in customers)
        {
            invoicesByCustomer.TryGetValue(customer.Id, out var customerInvoices);
            customerInvoices ??= [];
            receiptsByCustomer.TryGetValue(customer.Id, out var totalReceipts);

            var buckets = AgeCustomerReceivables(
                customer.OpeningBalance,
                customerInvoices.Select(i => (i.InvoiceDate, i.InvoiceType, i.NetTotal)),
                totalReceipts,
                asOf);

            if (buckets.Total == 0m)
            {
                continue;
            }

            lines.Add(new ArAgingLineDto(
                customer.Id,
                customer.BuyerId,
                customer.BuyerName,
                buckets.OpeningBalance,
                buckets.Current,
                buckets.Days31To60,
                buckets.Days61To90,
                buckets.Over90,
                buckets.Total));
        }

        return new ArAgingSummaryReportDto(
            asOf,
            lines.Count,
            lines.Sum(l => l.OpeningBalance),
            lines.Sum(l => l.Current),
            lines.Sum(l => l.Days31To60),
            lines.Sum(l => l.Days61To90),
            lines.Sum(l => l.Over90),
            lines.Sum(l => l.Total),
            lines);
    }

    private static AgingBuckets AgeCustomerReceivables(
        decimal openingBalance,
        IEnumerable<(DateTime InvoiceDate, InvoiceType InvoiceType, decimal NetTotal)> invoices,
        decimal totalReceipts,
        DateTime asOf)
    {
        var receivables = new List<(DateTime Date, decimal Amount)>();
        var creditPool = totalReceipts;

        if (openingBalance > 0m)
        {
            receivables.Add((DateTime.MinValue, openingBalance));
        }
        else if (openingBalance < 0m)
        {
            creditPool += Math.Abs(openingBalance);
        }

        foreach (var invoice in invoices.OrderBy(i => i.InvoiceDate))
        {
            if (invoice.InvoiceType == InvoiceType.CreditNote)
            {
                creditPool += invoice.NetTotal;
                continue;
            }

            receivables.Add((invoice.InvoiceDate.Date, invoice.NetTotal));
        }

        receivables = receivables.OrderBy(r => r.Date).ToList();

        var remaining = new List<(DateTime Date, decimal Amount)>();
        foreach (var (date, amount) in receivables)
        {
            var balance = amount;
            if (creditPool > 0m)
            {
                var applied = Math.Min(balance, creditPool);
                balance -= applied;
                creditPool -= applied;
            }

            if (balance > 0m)
            {
                remaining.Add((date, balance));
            }
        }

        var buckets = new AgingBuckets();
        foreach (var (date, amount) in remaining)
        {
            if (date == DateTime.MinValue)
            {
                buckets.OpeningBalance += amount;
                continue;
            }

            var days = (asOf - date).Days;
            if (days <= 30)
            {
                buckets.Current += amount;
            }
            else if (days <= 60)
            {
                buckets.Days31To60 += amount;
            }
            else if (days <= 90)
            {
                buckets.Days61To90 += amount;
            }
            else
            {
                buckets.Over90 += amount;
            }
        }

        return buckets;
    }

    private sealed class AgingBuckets
    {
        public decimal OpeningBalance { get; set; }
        public decimal Current { get; set; }
        public decimal Days31To60 { get; set; }
        public decimal Days61To90 { get; set; }
        public decimal Over90 { get; set; }
        public decimal Total => OpeningBalance + Current + Days31To60 + Days61To90 + Over90;
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
                a.SubTypeId,
                a.ParentAccountId,
                a.AccountType != null ? a.AccountType.TypeName : null,
                a.SubAccountType != null ? a.SubAccountType.SubTypeName : null,
                a.OpeningBalance))
            .ToListAsync(cancellationToken);
    }

    private static List<FinancialReportRowDto> BuildTrialBalanceRows(
        IReadOnlyList<TrialBalanceLineDto> lines,
        Dictionary<int, AccountRow> accountById)
    {
        var rows = new List<FinancialReportRowDto>();

        foreach (var line in lines)
        {
            accountById.TryGetValue(line.AccountId, out var account);
            var label = account is null
                ? $"{line.AccountNumber} – {line.AccountName}"
                : FormatAccountLabel(account, accountById);

            rows.Add(new FinancialReportRowDto(
                label,
                0,
                FinancialReportRowKind.Account,
                line.ClosingDebit > 0m ? line.ClosingDebit : null,
                line.ClosingCredit > 0m ? line.ClosingCredit : null,
                null));
        }

        rows.Add(new FinancialReportRowDto(
            "TOTAL",
            0,
            FinancialReportRowKind.Total,
            lines.Sum(l => l.ClosingDebit),
            lines.Sum(l => l.ClosingCredit),
            null));

        return rows;
    }

    private List<FinancialReportRowDto> BuildProfitAndLossRows(
        IReadOnlyList<AccountRow> accounts,
        Dictionary<int, JournalAggregate> journalByAccount,
        DateTime from,
        DateTime to,
        decimal totalRevenue,
        decimal totalCogs,
        decimal totalExpenses,
        decimal grossProfit,
        decimal netProfit)
    {
        var rows = new List<FinancialReportRowDto>();
        var accountById = accounts.ToDictionary(a => a.Id);
        var childrenLookup = accounts.ToLookup(a => a.ParentAccountId);
        var amountsByAccountId = BuildPeriodAmountsByAccount(accounts, journalByAccount, from, to);

        rows.Add(SectionRow("Ordinary Income/Expense", 0));

        AppendProfitAndLossSection(
            rows,
            "Income",
            accounts.Where(a => a.TypeId == RevenueTypeId),
            accountById,
            childrenLookup,
            amountsByAccountId,
            "Total Income",
            totalRevenue);

        AppendProfitAndLossSection(
            rows,
            "Cost of Goods Sold",
            accounts.Where(a => a.TypeId == CogsTypeId),
            accountById,
            childrenLookup,
            amountsByAccountId,
            "Total COGS",
            totalCogs);

        rows.Add(AmountRow("Gross Profit", 0, FinancialReportRowKind.Subtotal, grossProfit));

        AppendProfitAndLossSection(
            rows,
            "Expense",
            accounts.Where(a => a.TypeId == ExpenseTypeId),
            accountById,
            childrenLookup,
            amountsByAccountId,
            "Total Expense",
            totalExpenses);

        rows.Add(AmountRow("Net Ordinary Income", 0, FinancialReportRowKind.Subtotal, netProfit));
        rows.Add(AmountRow("Net Income", 0, FinancialReportRowKind.Total, netProfit));

        return rows;
    }

    private static void AppendProfitAndLossSection(
        List<FinancialReportRowDto> rows,
        string sectionTitle,
        IEnumerable<AccountRow> sectionAccounts,
        Dictionary<int, AccountRow> accountById,
        ILookup<int?, AccountRow> childrenLookup,
        Dictionary<int, decimal> amountsByAccountId,
        string sectionTotalLabel,
        decimal sectionTotal)
    {
        var sectionList = sectionAccounts.ToList();
        if (sectionList.Count == 0 && sectionTotal == 0m)
        {
            return;
        }

        rows.Add(SectionRow(sectionTitle, 0));

        var sectionIds = sectionList.Select(a => a.Id).ToHashSet();
        var roots = sectionList
            .Where(a => a.ParentAccountId is null || !sectionIds.Contains(a.ParentAccountId.Value))
            .OrderBy(a => a.AccountNumber)
            .ToList();

        foreach (var root in roots)
        {
            AppendAmountTreeRows(rows, root, childrenLookup, accountById, amountsByAccountId, 1);
        }

        if (sectionTotal != 0m || roots.Count > 0)
        {
            rows.Add(AmountRow(sectionTotalLabel, 0, FinancialReportRowKind.Subtotal, sectionTotal));
        }
    }

    private List<FinancialReportRowDto> BuildBalanceSheetRows(
        IReadOnlyList<AccountRow> accounts,
        Dictionary<int, JournalAggregate> journalByAccount,
        DateTime asOf,
        decimal netIncomeYtd,
        decimal retainedEarningsGap,
        decimal totalAssets,
        decimal totalLiabilities,
        decimal totalEquity,
        decimal totalLiabilitiesAndEquity)
    {
        var rows = new List<FinancialReportRowDto>();
        var accountById = accounts.ToDictionary(a => a.Id);
        var childrenLookup = accounts.ToLookup(a => a.ParentAccountId);
        var amountsByAccountId = BuildBalanceSheetAmountsByAccount(accounts, journalByAccount, asOf);

        rows.Add(SectionRow("ASSETS", 0));

        var assetAccounts = accounts.Where(a => a.TypeId == AssetTypeId).ToList();
        var currentAssetSubTypeIds = new HashSet<int> { 1, 2, 3, 4, 6, 7 };
        var currentAssets = assetAccounts
            .Where(a => a.SubTypeId is null || currentAssetSubTypeIds.Contains(a.SubTypeId.Value))
            .ToList();
        var fixedAssets = assetAccounts.Where(a => a.SubTypeId == 5).ToList();

        var currentAssetTotal = SumAccountAmounts(currentAssets, amountsByAccountId);
        AppendBalanceSheetAssetGroup(rows, "Current Assets", currentAssets, accountById, childrenLookup, amountsByAccountId, currentAssetTotal);
        rows.Add(AmountRow("Total Current Assets", 1, FinancialReportRowKind.Subtotal, currentAssetTotal));

        var fixedAssetTotal = SumAccountAmounts(fixedAssets, amountsByAccountId);
        if (fixedAssets.Count > 0 || fixedAssetTotal != 0m)
        {
            rows.Add(SectionRow("Fixed Assets", 1));
            var fixedIds = fixedAssets.Select(a => a.Id).ToHashSet();
            foreach (var root in fixedAssets
                         .Where(a => a.ParentAccountId is null || !fixedIds.Contains(a.ParentAccountId.Value))
                         .OrderBy(a => a.AccountNumber))
            {
                AppendAmountTreeRows(rows, root, childrenLookup, accountById, amountsByAccountId, 2);
            }

            rows.Add(AmountRow("Total Fixed Assets", 1, FinancialReportRowKind.Subtotal, fixedAssetTotal));
        }

        rows.Add(AmountRow("TOTAL ASSETS", 0, FinancialReportRowKind.Total, totalAssets));

        rows.Add(SectionRow("LIABILITIES & EQUITY", 0));
        rows.Add(SectionRow("Liabilities", 1));

        var liabilityAccounts = accounts.Where(a => a.TypeId == LiabilityTypeId).ToList();
        var currentLiabilities = liabilityAccounts
            .Where(a => a.SubTypeId is null || a.SubTypeId != 12)
            .ToList();
        var liabilityTotal = SumAccountAmounts(liabilityAccounts, amountsByAccountId);

        AppendBalanceSheetLiabilityGroup(
            rows,
            "Current Liabilities",
            currentLiabilities,
            accountById,
            childrenLookup,
            amountsByAccountId);

        rows.Add(AmountRow("Total Liabilities", 1, FinancialReportRowKind.Subtotal, liabilityTotal));

        rows.Add(SectionRow("Equity", 1));

        var equityAccounts = accounts
            .Where(a => a.TypeId == EquityTypeId
                        && !string.Equals(a.AccountNumber, OpeningBalanceEquity, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var root in equityAccounts
                     .Where(a => a.ParentAccountId is null)
                     .OrderBy(a => a.AccountNumber))
        {
            AppendAmountTreeRows(rows, root, childrenLookup, accountById, amountsByAccountId, 2);
        }

        if (netIncomeYtd != 0m)
        {
            rows.Add(AmountRow("Net Income", 2, FinancialReportRowKind.Account, netIncomeYtd));
        }

        if (Math.Abs(retainedEarningsGap) >= 0.01m)
        {
            rows.Add(AmountRow("Retained Earnings", 2, FinancialReportRowKind.Account, retainedEarningsGap));
        }

        rows.Add(AmountRow("Total Equity", 1, FinancialReportRowKind.Subtotal, totalEquity));
        rows.Add(AmountRow("TOTAL LIABILITIES & EQUITY", 0, FinancialReportRowKind.Total, totalLiabilitiesAndEquity));

        return rows;
    }

    private static void AppendBalanceSheetAssetGroup(
        List<FinancialReportRowDto> rows,
        string groupTitle,
        IReadOnlyList<AccountRow> groupAccounts,
        Dictionary<int, AccountRow> accountById,
        ILookup<int?, AccountRow> childrenLookup,
        Dictionary<int, decimal> amountsByAccountId,
        decimal groupTotal)
    {
        if (groupAccounts.Count == 0 && groupTotal == 0m)
        {
            return;
        }

        rows.Add(SectionRow(groupTitle, 1));

        var groupIds = groupAccounts.Select(a => a.Id).ToHashSet();
        var subtypeGroups = groupAccounts
            .GroupBy(a => a.SubTypeName ?? "Other")
            .OrderBy(g => g.Min(a => a.AccountNumber));

        foreach (var subtypeGroup in subtypeGroups)
        {
            var subtypeAccounts = subtypeGroup.ToList();
            var subtypeTotal = SumAccountAmounts(subtypeAccounts, amountsByAccountId);
            if (subtypeTotal == 0m && !subtypeAccounts.Any(a => HasTreeActivity(a, childrenLookup, amountsByAccountId, groupIds)))
            {
                continue;
            }

            var subtypeLabel = MapAssetSubtypeLabel(subtypeGroup.Key);
            rows.Add(SectionRow(subtypeLabel, 2));

            var roots = subtypeAccounts
                .Where(a => a.ParentAccountId is null || !groupIds.Contains(a.ParentAccountId.Value))
                .OrderBy(a => a.AccountNumber);

            foreach (var root in roots)
            {
                AppendAmountTreeRows(rows, root, childrenLookup, accountById, amountsByAccountId, 3);
            }

            if (subtypeAccounts.Count(a => a.ParentAccountId is not null) > 0 || roots.Any())
            {
                var firstRoot = roots.FirstOrDefault();
                if (firstRoot is not null && childrenLookup[firstRoot.Id].Any())
                {
                    rows.Add(AmountRow($"Total {FormatAccountLabel(firstRoot, accountById)}", 3, FinancialReportRowKind.Subtotal, subtypeTotal));
                }
            }
        }
    }

    private static void AppendBalanceSheetLiabilityGroup(
        List<FinancialReportRowDto> rows,
        string groupTitle,
        IReadOnlyList<AccountRow> groupAccounts,
        Dictionary<int, AccountRow> accountById,
        ILookup<int?, AccountRow> childrenLookup,
        Dictionary<int, decimal> amountsByAccountId)
    {
        if (groupAccounts.Count == 0)
        {
            return;
        }

        rows.Add(SectionRow(groupTitle, 2));

        var groupIds = groupAccounts.Select(a => a.Id).ToHashSet();
        var apAccounts = groupAccounts.Where(a => a.SubTypeId == 8).ToList();
        var otherAccounts = groupAccounts.Where(a => a.SubTypeId != 8).ToList();

        if (apAccounts.Count > 0)
        {
            rows.Add(SectionRow("Accounts Payable", 3));
            foreach (var root in apAccounts
                         .Where(a => a.ParentAccountId is null || !groupIds.Contains(a.ParentAccountId.Value))
                         .OrderBy(a => a.AccountNumber))
            {
                AppendAmountTreeRows(rows, root, childrenLookup, accountById, amountsByAccountId, 4);
            }

            var apTotal = SumAccountAmounts(apAccounts, amountsByAccountId);
            if (apTotal != 0m)
            {
                rows.Add(AmountRow("Total Accounts Payable", 3, FinancialReportRowKind.Subtotal, apTotal));
            }
        }

        if (otherAccounts.Count > 0)
        {
            rows.Add(SectionRow("Other Current Liabilities", 3));
            foreach (var root in otherAccounts
                         .Where(a => a.ParentAccountId is null || !groupIds.Contains(a.ParentAccountId.Value))
                         .OrderBy(a => a.AccountNumber))
            {
                AppendAmountTreeRows(rows, root, childrenLookup, accountById, amountsByAccountId, 4);
            }

            var otherTotal = SumAccountAmounts(otherAccounts, amountsByAccountId);
            if (otherTotal != 0m)
            {
                rows.Add(AmountRow("Total Other Current Liabilities", 3, FinancialReportRowKind.Subtotal, otherTotal));
            }
        }

        var groupTotal = SumAccountAmounts(groupAccounts, amountsByAccountId);
        rows.Add(AmountRow($"Total {groupTitle}", 2, FinancialReportRowKind.Subtotal, groupTotal));
    }

    private static Dictionary<int, decimal> BuildPeriodAmountsByAccount(
        IReadOnlyList<AccountRow> accounts,
        Dictionary<int, JournalAggregate> journalByAccount,
        DateTime from,
        DateTime to)
    {
        var amounts = new Dictionary<int, decimal>();

        foreach (var account in accounts)
        {
            var journal = journalByAccount.GetValueOrDefault(account.Id) ?? EmptyJournal;
            var periodDebit = journal.PeriodDebit(from, to);
            var periodCredit = journal.PeriodCredit(from, to);
            var amount = account.TypeId switch
            {
                RevenueTypeId => periodCredit - periodDebit,
                CogsTypeId or ExpenseTypeId => periodDebit - periodCredit,
                _ => periodDebit - periodCredit
            };

            if (amount != 0m)
            {
                amounts[account.Id] = amount;
            }
        }

        return amounts;
    }

    private static decimal CalculateCumulativeNetIncome(
        IReadOnlyList<AccountRow> accounts,
        Dictionary<int, JournalAggregate> journalByAccount,
        DateTime asOf)
    {
        decimal netIncome = 0m;

        foreach (var account in accounts.Where(a => a.TypeId is RevenueTypeId or CogsTypeId or ExpenseTypeId))
        {
            var journal = journalByAccount.GetValueOrDefault(account.Id) ?? EmptyJournal;
            var net = account.OpeningBalance + journal.UpToToDebit(asOf) - journal.UpToToCredit(asOf);
            netIncome -= net;
        }

        return Math.Round(netIncome, 2);
    }

    private static Dictionary<int, decimal> BuildBalanceSheetAmountsByAccount(
        IReadOnlyList<AccountRow> accounts,
        Dictionary<int, JournalAggregate> journalByAccount,
        DateTime asOf)
    {
        var amounts = new Dictionary<int, decimal>();

        foreach (var account in accounts.Where(a =>
                     a.TypeId is AssetTypeId or LiabilityTypeId or EquityTypeId
                     && !string.Equals(a.AccountNumber, OpeningBalanceEquity, StringComparison.OrdinalIgnoreCase)))
        {
            var journal = journalByAccount.GetValueOrDefault(account.Id) ?? EmptyJournal;
            var net = GlAccountBalance.ComputeNet(
                account.OpeningBalance,
                journal.UpToToDebit(asOf),
                journal.UpToToCredit(asOf),
                account.TypeId,
                account.AccountNumber);
            var amount = GlBalanceDisplay.NormalizeNetForDisplay(net, account.TypeId, account.AccountNumber);

            if (amount != 0m)
            {
                amounts[account.Id] = amount;
            }
        }

        return amounts;
    }

    private static void AppendAmountTreeRows(
        List<FinancialReportRowDto> rows,
        AccountRow account,
        ILookup<int?, AccountRow> childrenLookup,
        Dictionary<int, AccountRow> accountById,
        Dictionary<int, decimal> amountsByAccountId,
        int indentLevel)
    {
        var children = childrenLookup[account.Id].OrderBy(c => c.AccountNumber).ToList();
        amountsByAccountId.TryGetValue(account.Id, out var ownAmount);

        if (children.Count == 0)
        {
            if (ownAmount != 0m)
            {
                rows.Add(AmountRow(FormatAccountLabel(account, accountById), indentLevel, FinancialReportRowKind.Account, ownAmount));
            }

            return;
        }

        rows.Add(SectionRow(FormatAccountLabel(account, accountById), indentLevel));

        foreach (var child in children)
        {
            AppendAmountTreeRows(rows, child, childrenLookup, accountById, amountsByAccountId, indentLevel + 1);
        }

        var groupTotal = SumTreeAmount(account, childrenLookup, amountsByAccountId);
        if (groupTotal != 0m)
        {
            rows.Add(AmountRow($"Total {FormatAccountLabel(account, accountById)}", indentLevel, FinancialReportRowKind.Subtotal, groupTotal));
        }
    }

    private static decimal SumTreeAmount(
        AccountRow account,
        ILookup<int?, AccountRow> childrenLookup,
        Dictionary<int, decimal> amountsByAccountId)
    {
        amountsByAccountId.TryGetValue(account.Id, out var total);

        foreach (var child in childrenLookup[account.Id])
        {
            total += SumTreeAmount(child, childrenLookup, amountsByAccountId);
        }

        return total;
    }

    private static decimal SumAccountAmounts(
        IEnumerable<AccountRow> accounts,
        Dictionary<int, decimal> amountsByAccountId)
    {
        return accounts.Sum(a => amountsByAccountId.GetValueOrDefault(a.Id));
    }

    private static bool HasTreeActivity(
        AccountRow account,
        ILookup<int?, AccountRow> childrenLookup,
        Dictionary<int, decimal> amountsByAccountId,
        HashSet<int>? scopeIds = null)
    {
        if (amountsByAccountId.ContainsKey(account.Id))
        {
            return true;
        }

        return childrenLookup[account.Id].Any(child =>
            scopeIds is null || scopeIds.Contains(child.Id)
                ? HasTreeActivity(child, childrenLookup, amountsByAccountId, scopeIds)
                : false);
    }

    private static string FormatAccountLabel(AccountRow account, Dictionary<int, AccountRow> accountById)
    {
        if (account.ParentAccountId is null
            || !accountById.TryGetValue(account.ParentAccountId.Value, out var parent))
        {
            return $"{account.AccountNumber} – {account.AccountName}";
        }

        return $"{parent.AccountNumber} – {parent.AccountName}:{account.AccountNumber} – {account.AccountName}";
    }

    private static string MapAssetSubtypeLabel(string subtypeName) =>
        subtypeName switch
        {
            "Cash & Bank" => "Checking/Savings",
            "Accounts Receivable" => "Accounts Receivable",
            "Fixed Assets" => "Fixed Assets",
            _ => "Other Current Assets"
        };

    private static FinancialReportRowDto SectionRow(string label, int indentLevel) =>
        new(label, indentLevel, FinancialReportRowKind.SectionHeader, null, null, null);

    private static FinancialReportRowDto AmountRow(
        string label,
        int indentLevel,
        FinancialReportRowKind kind,
        decimal amount) =>
        new(label, indentLevel, kind, null, null, amount);

    private async Task<Dictionary<int, JournalAggregate>> GetJournalTotalsByAccountAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var entries = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l => l.JournalEntry.CompanyId == companyId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && !l.JournalEntry.IsDeleted)
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
        int? SubTypeId,
        int? ParentAccountId,
        string? TypeName,
        string? SubTypeName,
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
