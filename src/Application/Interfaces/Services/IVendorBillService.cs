using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IVendorBillService
{
    Task<DataTableResponse<VendorBillListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    Task<VendorBillDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken = default);

    Task<NextVendorBillNumberDto> GenerateNextBillNumberAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VendorBillVendorLookupDto>> GetVendorLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VendorBillItemLookupDto>> GetItemLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VendorBillWarehouseLookupDto>> GetWarehouseLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<VendorBillPurchaseTaxSettingsDto> GetPurchaseTaxSettingsAsync(
        CancellationToken cancellationToken = default);

    Task<VendorBillSaveResult> CreateAsync(
        VendorBillSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<VendorBillSaveResult> UpdateAsync(
        VendorBillSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<VendorBillActionResult> ApproveAsync(int id, CancellationToken cancellationToken = default);

    Task<VendorBillActionResult> CancelAsync(int id, CancellationToken cancellationToken = default);

    Task<VendorBillActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
