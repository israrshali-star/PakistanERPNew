using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IBankReconciliationService
{
    Task<DataTableResponse<BankReconciliationListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<BankReconciliationPreviewDto?> GetPreviewAsync(int bankId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BankReconciliationBankLookupDto>> GetBankLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<BankReconciliationCompleteResult> CompleteAsync(
        BankReconciliationCompleteRequest request,
        CancellationToken cancellationToken = default);
}
