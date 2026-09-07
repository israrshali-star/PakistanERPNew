using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Common;

public static class CustomerReceiptBalanceRules
{
    public static bool IsChequeCleared(CustomerReceiptStatus status, DateTime? clearedAt) =>
        status == CustomerReceiptStatus.Cleared && clearedAt.HasValue;

    public static bool AffectsCustomerBalance(CustomerReceipt receipt) =>
        AffectsCustomerBalance(receipt.PaymentMethod, receipt.Status, receipt.ClearedAt);

    public static bool IsChequeReturned(CustomerReceiptStatus status) =>
        status == CustomerReceiptStatus.Returned;

    public static bool AffectsCustomerBalance(
        PaymentMethod paymentMethod,
        CustomerReceiptStatus status,
        DateTime? clearedAt) =>
        !IsChequeReturned(status)
        && (paymentMethod != PaymentMethod.Cheque || IsChequeCleared(status, clearedAt));

    /// <summary>
    /// Other-bank cheques lock after deposit or bank clearance because they belong to a deposit batch.
    /// Same-bank cheques post directly to the bank (like cash) and stay editable.
    /// </summary>
    public static bool IsLockedFromModification(
        PaymentMethod paymentMethod,
        ChequeBankType? chequeBankType,
        CustomerReceiptStatus status,
        DateTime? clearedAt,
        bool isDeposited)
    {
        if (IsChequeReturned(status))
        {
            return true;
        }

        if (paymentMethod != PaymentMethod.Cheque || chequeBankType == ChequeBankType.SameBank)
        {
            return false;
        }

        return isDeposited || IsChequeCleared(status, clearedAt);
    }
}
