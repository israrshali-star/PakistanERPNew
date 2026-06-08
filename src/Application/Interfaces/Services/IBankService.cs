using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IBankService
{
    Task<DataTableResponse<BankListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<BankDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BankChartOfAccountLookupDto>> GetChartOfAccountLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BankLookupDto>> GetActiveBankLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<BankSaveResult> CreateAsync(BankSaveRequest request, CancellationToken cancellationToken = default);

    Task<BankSaveResult> UpdateAsync(BankSaveRequest request, CancellationToken cancellationToken = default);

    Task<BankSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
