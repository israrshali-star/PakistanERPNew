using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IVendorPaymentService
{
    Task<DataTableResponse<VendorPaymentListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    Task<VendorPaymentDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<NextVendorPaymentNumberDto> GenerateNextPaymentNumberAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VendorPaymentVendorLookupDto>> GetVendorLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VendorPaymentBankLookupDto>> GetBankLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<VendorPaymentSaveResult> CreateAsync(
        VendorPaymentSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<VendorPaymentSaveResult> UpdateAsync(
        VendorPaymentSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<VendorPaymentSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
