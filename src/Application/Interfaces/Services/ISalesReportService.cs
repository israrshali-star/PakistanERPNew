using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ISalesReportService
{
    Task<SalesRegisterReportDto> GetSalesRegisterAsync(
        SalesReportRequest request,
        CancellationToken cancellationToken = default);

    Task<SalesByCustomerReportDto> GetSalesByCustomerAsync(
        SalesReportRequest request,
        CancellationToken cancellationToken = default);

    Task<SalesTaxSummaryReportDto> GetSalesTaxSummaryAsync(
        SalesReportRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesReportCustomerLookupDto>> GetCustomerLookupsAsync(
        CancellationToken cancellationToken = default);
}
