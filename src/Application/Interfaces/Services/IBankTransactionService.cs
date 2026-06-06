using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IBankTransactionService
{
    Task<DataTableResponse<BankTransactionListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        int? bankId = null,
        CancellationToken cancellationToken = default);

    Task<BankTransactionDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BankTransactionBankLookupDto>> GetBankLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<BankTransactionSaveResult> CreateAsync(
        BankTransactionSaveRequest request,
        CancellationToken cancellationToken = default);
}
