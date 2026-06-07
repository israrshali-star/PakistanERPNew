using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IStackLotInventoryService
{
    Task<StackLotAvailabilityDto?> GetAvailabilityAsync(
        int itemId,
        string? stackNo,
        string? lotNo,
        int? excludeInvoiceId = null,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string? Message)> ValidateSaleLinesAsync(
        InvoiceType invoiceType,
        IReadOnlyList<StackLotSaleValidationLine> lines,
        int? excludeInvoiceId = null,
        CancellationToken cancellationToken = default);
}
