using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ICustomerReceiptInvoiceAllocationService
{
    Task<CustomerReceiptInvoiceAllocationDto?> GetAllocationAsync(
        int customerId,
        DateTime receiptDate,
        decimal amount,
        int? receiptId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, decimal>> GetRemainingByInvoiceIdAsync(
        IReadOnlyCollection<int> customerIds,
        CancellationToken cancellationToken = default);
}
