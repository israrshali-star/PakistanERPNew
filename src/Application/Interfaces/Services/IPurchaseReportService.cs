using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IPurchaseReportService
{
    Task<PurchaseRegisterReportDto> GetPurchaseRegisterAsync(
        PurchaseReportRequest request,
        CancellationToken cancellationToken = default);

    Task<PurchaseByVendorReportDto> GetPurchaseByVendorAsync(
        PurchaseReportRequest request,
        CancellationToken cancellationToken = default);

    Task<InputTaxSummaryReportDto> GetInputTaxSummaryAsync(
        PurchaseReportRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseReportVendorLookupDto>> GetVendorLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<StackLotTrackingReportDto> GetStackLotTrackingAsync(
        StackLotTrackingRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StackLotReportItemLookupDto>> GetStackLotItemLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<StackLotFilterLookupDto> GetStackLotFilterLookupsAsync(
        int? itemId,
        CancellationToken cancellationToken = default);
}
