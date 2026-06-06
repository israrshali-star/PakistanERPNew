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
}
