using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ILedgerShareService
{
    Task<LedgerShareInfoDto?> GetCustomerShareInfoAsync(
        int customerId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    Task<LedgerShareInfoDto?> GetVendorShareInfoAsync(
        int vendorId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    Task<byte[]?> GetCustomerLedgerPdfAsync(
        int customerId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    Task<byte[]?> GetVendorLedgerPdfAsync(
        int vendorId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    Task<LedgerShareActionResult> SendCustomerLedgerEmailAsync(
        int customerId,
        LedgerEmailShareRequest request,
        CancellationToken cancellationToken = default);

    Task<LedgerShareActionResult> SendVendorLedgerEmailAsync(
        int vendorId,
        LedgerEmailShareRequest request,
        CancellationToken cancellationToken = default);
}
