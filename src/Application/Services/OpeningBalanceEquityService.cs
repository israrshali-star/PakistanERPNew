using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class OpeningBalanceEquityService : IOpeningBalanceEquityService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public OpeningBalanceEquityService(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task EnsureBalancedAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var obeAccount = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(
                a => a.CompanyId == companyId
                     && a.AccountNumber == GlAccountNumbers.OpeningBalanceEquity
                     && !a.IsDeleted,
                cancellationToken);

        if (obeAccount is null)
        {
            return;
        }

        var otherAccounts = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.Id != obeAccount.Id && a.IsActive && !a.IsDeleted)
            .Select(a => new { a.Id, a.OpeningBalance, a.TypeId, a.AccountNumber })
            .ToListAsync(cancellationToken);

        var journalTotals = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l =>
                l.JournalEntry.CompanyId == companyId
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

        var journalByAccount = journalTotals.ToDictionary(x => x.AccountId);
        journalByAccount.TryGetValue(obeAccount.Id, out var obeJournal);

        var otherTotals = otherAccounts.Select(account =>
        {
            journalByAccount.TryGetValue(account.Id, out var journal);
            return new OpeningBalanceEquityBalancer.AccountTotals(
                account.Id,
                account.OpeningBalance,
                journal?.Debit ?? 0m,
                journal?.Credit ?? 0m,
                account.TypeId,
                account.AccountNumber);
        });

        var requiredOpening = OpeningBalanceEquityBalancer.ComputeRequiredOpening(
            otherTotals,
            obeJournal?.Debit ?? 0m,
            obeJournal?.Credit ?? 0m,
            companyId);

        if (obeAccount.OpeningBalance == requiredOpening)
        {
            return;
        }

        obeAccount.OpeningBalance = requiredOpening;
        obeAccount.UpdatedAt = DateTime.UtcNow;
        obeAccount.UpdatedBy = _currentUser.UserName ?? "obe-auto-balance";
        _unitOfWork.Repository<ChartOfAccount>().Update(obeAccount);
    }
}
