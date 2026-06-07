using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IVendorGlPostingService
{
    Task<GlPostingResult> SyncVendorOpeningBalanceAsync(
        int vendorId,
        string vendorName,
        decimal openingBalance,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> RemoveVendorOpeningBalanceAsync(
        int vendorId,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> PostVendorPaymentAsync(
        VendorPayment payment,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> SyncVendorPaymentAsync(
        VendorPayment payment,
        decimal previousAmount,
        int? previousBankId,
        Domain.Enums.PaymentMethod previousPaymentMethod,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> RemoveVendorPaymentAsync(
        int paymentId,
        CancellationToken cancellationToken = default);
}
