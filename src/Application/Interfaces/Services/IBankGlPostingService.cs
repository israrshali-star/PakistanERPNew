using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IBankGlPostingService
{
    Task<int?> EnsureUndepositedFundsAccountAsync(int companyId, CancellationToken cancellationToken = default);

    Task<decimal> GetAccountBalanceAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> PostBankTransactionAsync(
        BankTransaction transaction,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> RemoveBankTransactionAsync(
        int bankTransactionId,
        CancellationToken cancellationToken = default);
}
