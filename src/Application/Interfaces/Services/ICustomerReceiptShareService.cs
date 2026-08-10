using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ICustomerReceiptShareService
{
    Task<CustomerReceiptShareInfoDto?> GetShareInfoAsync(int receiptId, CancellationToken cancellationToken = default);

    Task<byte[]?> GetReceiptPdfAsync(
        int receiptId,
        bool useUrdu = false,
        CancellationToken cancellationToken = default);
}
