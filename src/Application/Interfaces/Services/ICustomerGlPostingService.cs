using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ICustomerGlPostingService
{
    Task<GlPostingResult> SyncCustomerOpeningBalanceAsync(
        int customerId,
        string buyerName,
        decimal openingBalance,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> RemoveCustomerOpeningBalanceAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> PostCustomerReceiptAsync(
        CustomerReceipt receipt,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> SyncCustomerReceiptAsync(
        CustomerReceipt receipt,
        decimal previousAmount,
        int? previousBankId,
        Domain.Enums.PaymentMethod previousPaymentMethod,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> RemoveCustomerReceiptAsync(
        int receiptId,
        CancellationToken cancellationToken = default);
}
