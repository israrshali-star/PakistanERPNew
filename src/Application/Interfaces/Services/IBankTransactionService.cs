using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IBankTransactionService
{
    Task<DataTableResponse<BankTransactionListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        int? bankId = null,
        BankTransactionType? transactionType = null,
        CancellationToken cancellationToken = default);

    Task<BankTransactionDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BankCoaLookupDto>> GetBankCoaLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BankCoaLookupDto>> GetTransferCoaLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BankCoaLookupDto>> GetCounterCoaLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<BankUndepositedSummaryDto> GetUndepositedSummaryAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UndepositedChequeDto>> GetUndepositedChequesAsync(
        CancellationToken cancellationToken = default);

    Task<BankTransactionSaveResult> CreateAsync(
        BankTransactionSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<BankNextChequeNumberDto> GetNextChequeNumberAsync(
        int chartOfAccountId,
        CancellationToken cancellationToken = default);

    Task<BankNextChequeNumberSaveResult> SetNextChequeNumberAsync(
        BankNextChequeNumberSaveRequest request,
        CancellationToken cancellationToken = default);
}
