namespace PakistanAccountingERP.Application.Common;

/// <summary>English / Urdu labels for company 3 trade invoice and delivery challan PDFs.</summary>
public sealed class TradeDocumentPdfLabels
{
    public required string Date { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string Description { get; init; }
    public required string CartonDescription { get; init; }
    public required string NoOfCtn { get; init; }
    public required string Qty { get; init; }
    public required string Rate { get; init; }
    public required string Amount { get; init; }
    public required string Total { get; init; }
    public required string CustomerTotalBalance { get; init; }
    public required string SalesTax { get; init; }
    public required string DeliveryChallan { get; init; }
    public required string ShippingAddress { get; init; }
    public required string ItemDescription { get; init; }
    public required string LotNo { get; init; }
    public required string StackNo { get; init; }
    public required string CustomerSignature { get; init; }
    public required string Dear { get; init; }
    public required string PleaseFindInvoice { get; init; }
    public required string PleaseFindChallan { get; init; }
    public required string ClosingAmount { get; init; }
    public required string Customer { get; init; }
    public required string TotalCartons { get; init; }
    public required string TotalQuantity { get; init; }
    public required string Regards { get; init; }
    public required string WhatsAppInvoiceHint { get; init; }

    public static TradeDocumentPdfLabels English { get; } = new()
    {
        Date = "Date:",
        InvoiceNumber = "Invoice #:",
        Description = "Description",
        CartonDescription = "CTN Description",
        NoOfCtn = "No of Ctn",
        Qty = "QTY",
        Rate = "Rate",
        Amount = "Amount",
        Total = "Total",
        CustomerTotalBalance = "Customer Total Balance",
        SalesTax = "Sales Tax",
        DeliveryChallan = "Delivery Challan",
        ShippingAddress = "Shipping Address.",
        ItemDescription = "Item Description",
        LotNo = "Lot No.",
        StackNo = "Stack No.",
        CustomerSignature = "Customer Signature",
        Dear = "Dear",
        PleaseFindInvoice = "Please find attached invoice",
        PleaseFindChallan = "Please find attached delivery challan for dispatch.",
        ClosingAmount = "Amount",
        Customer = "Customer",
        TotalCartons = "Total cartons",
        TotalQuantity = "Total quantity",
        Regards = "Regards",
        WhatsAppInvoiceHint = "Please find the invoice PDF attached or request it from us."
    };

    public static TradeDocumentPdfLabels Urdu { get; } = new()
    {
        Date = "تاریخ:",
        InvoiceNumber = "انوائس نمبر:",
        Description = "تفصیل",
        CartonDescription = "کارٹن تفصیل",
        NoOfCtn = "کارٹن تعداد",
        Qty = "مقدار",
        Rate = "ریٹ",
        Amount = "رقم",
        Total = "کل",
        CustomerTotalBalance = "کسٹمر کل بیلنس",
        SalesTax = "سیلز ٹیکس",
        DeliveryChallan = "ڈیلیوری چالان",
        ShippingAddress = "شپنگ ایڈریس۔",
        ItemDescription = "آئٹم تفصیل",
        LotNo = "لاٹ نمبر",
        StackNo = "اسٹیک نمبر",
        CustomerSignature = "کسٹمر دستخط",
        Dear = "محترم",
        PleaseFindInvoice = "براہ کرم منسلک انوائس ملاحظہ فرمائیں",
        PleaseFindChallan = "براہ کرم ڈسپیچ کے لیے منسلک ڈیلیوری چالان ملاحظہ فرمائیں۔",
        ClosingAmount = "رقم",
        Customer = "کسٹمر",
        TotalCartons = "کل کارٹن",
        TotalQuantity = "کل مقدار",
        Regards = "شکریہ",
        WhatsAppInvoiceHint = "انوائس پی ڈی ایف منسلک ہے یا ہم سے طلب کیجیے۔"
    };

    public static TradeDocumentPdfLabels For(bool useUrdu) => useUrdu ? Urdu : English;
}
