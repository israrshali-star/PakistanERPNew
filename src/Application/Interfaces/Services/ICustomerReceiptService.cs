using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ICustomerReceiptService
{
    Task<DataTableResponse<CustomerReceiptListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<CustomerReceiptDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<NextReceiptNumberDto> GenerateNextReceiptNumberAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerReceiptCustomerLookupDto>> GetCustomerLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerReceiptBankLookupDto>> GetBankLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<CustomerReceiptSaveResult> CreateAsync(
        CustomerReceiptSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<CustomerReceiptSaveResult> UpdateAsync(
        CustomerReceiptSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<CustomerReceiptSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<CustomerReceiptSaveResult> ApproveClearanceAsync(
        int id,
        CustomerReceiptApproveClearanceRequest? request,
        CancellationToken cancellationToken = default);
}
