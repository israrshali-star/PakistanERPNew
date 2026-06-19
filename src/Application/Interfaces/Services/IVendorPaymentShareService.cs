using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IVendorPaymentShareService
{
    Task<VendorPaymentShareInfoDto?> GetShareInfoAsync(int paymentId, CancellationToken cancellationToken = default);

    Task<byte[]?> GetPaymentPdfAsync(int paymentId, CancellationToken cancellationToken = default);
}
