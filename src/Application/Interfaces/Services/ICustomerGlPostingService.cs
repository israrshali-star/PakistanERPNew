using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ICustomerGlPostingService
{
    Task<GlPostingResult> SyncCustomerOpeningBalanceAsync(
        int customerId,
        string buyerName,
        decimal openingBalance,
        DateTime? entryDate = null,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> RemoveCustomerOpeningBalanceAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> PostCustomerReceiptAsync(
        CustomerReceipt receipt,
        bool postUnclearedOtherBankCheque = false,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> SyncCustomerReceiptAsync(
        CustomerReceipt receipt,
        decimal previousAmount,
        int? previousBankId,
        Domain.Enums.PaymentMethod previousPaymentMethod,
        Domain.Enums.ChequeBankType? previousChequeBankType,
        bool postUnclearedOtherBankCheque = false,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> RemoveCustomerReceiptAsync(
        int receiptId,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> PostChequeClearanceAsync(
        CustomerReceipt receipt,
        int bankChartOfAccountId,
        CancellationToken cancellationToken = default);

    Task<GlPostingResult> PostChequeReturnAsync(
        CustomerReceipt receipt,
        CancellationToken cancellationToken = default);
}
