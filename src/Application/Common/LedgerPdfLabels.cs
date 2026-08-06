namespace PakistanAccountingERP.Application.Common;

/// <summary>English / Urdu label set for ledger PDF and share messages.</summary>
public sealed class LedgerPdfLabels
{
    public required string CustomerLedger { get; init; }
    public required string VendorLedger { get; init; }
    public required string CustomerStatement { get; init; }
    public required string VendorStatement { get; init; }
    public required string Opening { get; init; }
    public required string Closing { get; init; }
    public required string Date { get; init; }
    public required string Reference { get; init; }
    public required string Description { get; init; }
    public required string Debit { get; init; }
    public required string Credit { get; init; }
    public required string Pending { get; init; }
    public required string Balance { get; init; }
    public required string Ntn { get; init; }
    public required string Printed { get; init; }
    public required string PeriodPrefix { get; init; }
    public required string PeriodTo { get; init; }
    public required string FullLedgerAsOf { get; init; }
    public required string Dear { get; init; }
    public required string PleaseFindAttached { get; init; }
    public required string ClosingBalance { get; init; }
    public required string Regards { get; init; }
    public required string Party { get; init; }
    public required string Code { get; init; }
    public required string WhatsAppAttachHint { get; init; }

    public static LedgerPdfLabels English { get; } = new()
    {
        CustomerLedger = "Customer Ledger",
        VendorLedger = "Vendor Ledger",
        CustomerStatement = "Customer Statement",
        VendorStatement = "Vendor Statement",
        Opening = "Opening",
        Closing = "Closing",
        Date = "Date",
        Reference = "Reference",
        Description = "Description",
        Debit = "Debit",
        Credit = "Credit",
        Pending = "Pending",
        Balance = "Balance",
        Ntn = "NTN",
        Printed = "Printed",
        PeriodPrefix = "Period",
        PeriodTo = "to",
        FullLedgerAsOf = "Full ledger as of",
        Dear = "Dear",
        PleaseFindAttached = "Please find attached your",
        ClosingBalance = "Closing balance",
        Regards = "Regards",
        Party = "Party",
        Code = "Code",
        WhatsAppAttachHint = "Please find the ledger PDF attached or request it from us."
    };

    public static LedgerPdfLabels Urdu { get; } = new()
    {
        CustomerLedger = "کسٹمر لیجر",
        VendorLedger = "وینڈر لیجر",
        CustomerStatement = "کسٹمر اسٹیٹمنٹ",
        VendorStatement = "وینڈر اسٹیٹمنٹ",
        Opening = "ابتدائی بیلنس",
        Closing = "اختتامی بیلنس",
        Date = "تاریخ",
        Reference = "حوالہ",
        Description = "تفصیل",
        Debit = "ڈیبٹ",
        Credit = "کریڈٹ",
        Pending = "باقی",
        Balance = "بیلنس",
        Ntn = "این ٹی این",
        Printed = "پرنٹ",
        PeriodPrefix = "مدت",
        PeriodTo = "تا",
        FullLedgerAsOf = "مکمل لیجر بمورخہ",
        Dear = "محترم",
        PleaseFindAttached = "براہ کرم منسلک لیجر ملاحظہ فرمائیں",
        ClosingBalance = "اختتامی بیلنس",
        Regards = "شکریہ",
        Party = "پارٹی",
        Code = "کوڈ",
        WhatsAppAttachHint = "لیجر پی ڈی ایف منسلک ہے یا ہم سے طلب کیجیے۔"
    };

    public static LedgerPdfLabels For(bool useUrdu) => useUrdu ? Urdu : English;

    public string TitleFor(string partyType, bool isStatement)
    {
        var isCustomer = string.Equals(partyType, "customer", StringComparison.OrdinalIgnoreCase);
        if (isStatement)
        {
            return isCustomer ? CustomerStatement : VendorStatement;
        }

        return isCustomer ? CustomerLedger : VendorLedger;
    }

    public string BuildPeriodLabel(DateTime? fromDate, DateTime? toDate)
    {
        if (fromDate.HasValue && toDate.HasValue)
        {
            return $"{PeriodPrefix}: {fromDate.Value:dd/MM/yyyy} {PeriodTo} {toDate.Value:dd/MM/yyyy}";
        }

        return $"{FullLedgerAsOf} {DateTime.Today:dd/MM/yyyy}";
    }
}
