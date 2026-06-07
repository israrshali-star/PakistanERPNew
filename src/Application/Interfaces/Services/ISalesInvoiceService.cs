using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ISalesInvoiceService
{
    Task<DataTableResponse<SalesInvoiceListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<NextInvoiceNumberDto> GenerateNextInvoiceNumberAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesInvoiceCustomerLookupDto>> GetCustomerLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesInvoiceItemLookupDto>> GetItemLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceSaveResult> CreateAsync(
        SalesInvoiceSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken = default);

    Task<SalesInvoiceActionResult> PostAsync(int id, CancellationToken cancellationToken = default);

    Task<SalesInvoiceActionResult> CancelAsync(int id, CancellationToken cancellationToken = default);

    Task<SalesInvoiceActionResult> SubmitToFbrAsync(int id, CancellationToken cancellationToken = default);

    Task<FbrPayloadPreviewDto?> GetFbrPayloadPreviewAsync(int id, CancellationToken cancellationToken = default);

    Task<SalesInvoicePrintDto?> GetPrintDataAsync(int id, CancellationToken cancellationToken = default);
}
