namespace PakistanAccountingERP.Application.Common;

/// <summary>English / Urdu labels for customer receipt PDFs and share messages.</summary>
public sealed class CustomerReceiptPdfLabels
{
    public required string PaymentReceipt { get; init; }
    public required string ReceivedFrom { get; init; }
    public required string Date { get; init; }
    public required string PaymentMethod { get; init; }
    public required string CheckRefNo { get; init; }
    public required string AmountInWords { get; init; }
    public required string PaymentAmount { get; init; }
    public required string TotalAmountDue { get; init; }
    public required string InvoicesPaid { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string Amount { get; init; }
    public required string ReceiptNumber { get; init; }
    public required string Printed { get; init; }
    public required string Dear { get; init; }
    public required string Receipt { get; init; }
    public required string Customer { get; init; }
    public required string Payment { get; init; }
    public required string ChequeRef { get; init; }
    public required string RefNo { get; init; }
    public required string WhatsAppAttachHint { get; init; }
    public required string Regards { get; init; }

    public static CustomerReceiptPdfLabels English { get; } = new()
    {
        PaymentReceipt = "Payment Receipt",
        ReceivedFrom = "Received From",
        Date = "Date",
        PaymentMethod = "Payment Method",
        CheckRefNo = "Check/Ref No",
        AmountInWords = "Amount in words",
        PaymentAmount = "Payment Amount",
        TotalAmountDue = "Total Amount Due",
        InvoicesPaid = "Invoices Paid",
        InvoiceNumber = "Invoice #",
        Amount = "Amount",
        ReceiptNumber = "Receipt #",
        Printed = "Printed",
        Dear = "Dear",
        Receipt = "Receipt",
        Customer = "Customer",
        Payment = "Payment",
        ChequeRef = "Cheque/Ref #",
        RefNo = "Ref #",
        WhatsAppAttachHint = "Please find the receipt PDF attached or request it from us.",
        Regards = "Regards"
    };

    public static CustomerReceiptPdfLabels Urdu { get; } = new()
    {
        PaymentReceipt = "ادائیگی کی رسید",
        ReceivedFrom = "وصول کنندہ",
        Date = "تاریخ",
        PaymentMethod = "ادائیگی کا طریقہ",
        CheckRefNo = "چیک / حوالہ نمبر",
        AmountInWords = "رقم الفاظ میں",
        PaymentAmount = "ادائیگی کی رقم",
        TotalAmountDue = "کل واجب الادا رقم",
        InvoicesPaid = "ادا شدہ انوائسز",
        InvoiceNumber = "انوائس #",
        Amount = "رقم",
        ReceiptNumber = "رسید #",
        Printed = "پرنٹ",
        Dear = "محترم",
        Receipt = "رسید",
        Customer = "کسٹمر",
        Payment = "ادائیگی",
        ChequeRef = "چیک / حوالہ #",
        RefNo = "حوالہ #",
        WhatsAppAttachHint = "برائے مہربانی رسید پی ڈی ایف منسلک دیکھیں یا ہم سے طلب کریں۔",
        Regards = "مخلص"
    };

    public static CustomerReceiptPdfLabels For(bool useUrdu) => useUrdu ? Urdu : English;
}
