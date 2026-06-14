using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ISalesInvoiceService
{
    Task<DataTableResponse<SalesInvoiceListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    Task<NextInvoiceNumberDto> GenerateNextInvoiceNumberAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesInvoiceCustomerLookupDto>> GetCustomerLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesInvoiceItemLookupDto>> GetItemLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceTaxRatesDto> GetTaxRatesAsync(CancellationToken cancellationToken = default);

    Task<SalesInvoiceSaveResult> CreateAsync(
        SalesInvoiceSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceSaveResult> UpdateAsync(
        SalesInvoiceSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken = default);

    Task<SalesInvoiceActionResult> PostAsync(int id, CancellationToken cancellationToken = default);

    Task<SalesInvoiceActionResult> CancelAsync(int id, CancellationToken cancellationToken = default);

    Task<SalesInvoiceActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<SalesInvoiceActionResult> SubmitToFbrAsync(int id, CancellationToken cancellationToken = default);

    Task<FbrPayloadPreviewDto?> GetFbrPayloadPreviewAsync(int id, CancellationToken cancellationToken = default);

    Task<SalesInvoicePrintDto?> GetPrintDataAsync(int id, CancellationToken cancellationToken = default);

    Task<DeliveryChallanPrintDto?> GetDeliveryChallanDataAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<TradeInvoicePrintDto?> GetTradeInvoicePrintDataAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubmittedInvoicePrintListItemDto>> GetSubmittedInvoicesForPrintAsync(
        string? buyerName,
        string? invoiceNumber,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceBulkPdfResult> GenerateBulkInvoicePdfAsync(
        IReadOnlyList<int> invoiceIds,
        CancellationToken cancellationToken = default);
}
