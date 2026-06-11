using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Common;

public static class CustomerReceiptBalanceRules
{
    public static bool IsChequeCleared(CustomerReceiptStatus status, DateTime? clearedAt) =>
        status == CustomerReceiptStatus.Cleared && clearedAt.HasValue;

    public static bool AffectsCustomerBalance(CustomerReceipt receipt) =>
        AffectsCustomerBalance(receipt.PaymentMethod, receipt.Status, receipt.ClearedAt);

    public static bool AffectsCustomerBalance(
        PaymentMethod paymentMethod,
        CustomerReceiptStatus status,
        DateTime? clearedAt) =>
        paymentMethod != PaymentMethod.Cheque || IsChequeCleared(status, clearedAt);
}
