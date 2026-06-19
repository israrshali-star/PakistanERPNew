using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IInvoiceShareService
{
    Task<SalesInvoiceShareInfoDto?> GetShareInfoAsync(int invoiceId, CancellationToken cancellationToken = default);

    Task<SalesInvoiceShareActionResult> SendEmailAsync(
        int invoiceId,
        SalesInvoiceEmailShareRequest request,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceShareActionResult> SendDeliveryChallanEmailAsync(
        int invoiceId,
        SalesInvoiceChallanEmailShareRequest request,
        CancellationToken cancellationToken = default);
}
