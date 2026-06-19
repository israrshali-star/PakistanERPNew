using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Services;

public class LedgerShareService : ILedgerShareService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICustomerService _customerService;
    private readonly IVendorService _vendorService;
    private readonly ILedgerPdfService _ledgerPdfService;
    private readonly IEmailSender _emailSender;

    public LedgerShareService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICustomerService customerService,
        IVendorService vendorService,
        ILedgerPdfService ledgerPdfService,
        IEmailSender emailSender)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _customerService = customerService;
        _vendorService = vendorService;
        _ledgerPdfService = ledgerPdfService;
        _emailSender = emailSender;
    }

    public Task<LedgerShareInfoDto?> GetCustomerShareInfoAsync(
        int customerId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default) =>
        BuildShareInfoAsync("customer", customerId, fromDate, toDate, cancellationToken);

    public Task<LedgerShareInfoDto?> GetVendorShareInfoAsync(
        int vendorId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default) =>
        BuildShareInfoAsync("vendor", vendorId, fromDate, toDate, cancellationToken);

    public async Task<byte[]?> GetCustomerLedgerPdfAsync(
        int customerId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var pdfModel = await BuildCustomerPdfModelAsync(customerId, fromDate, toDate, cancellationToken);
        return pdfModel is null ? null : _ledgerPdfService.GeneratePdf(pdfModel);
    }

    public async Task<byte[]?> GetVendorLedgerPdfAsync(
        int vendorId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var pdfModel = await BuildVendorPdfModelAsync(vendorId, fromDate, toDate, cancellationToken);
        return pdfModel is null ? null : _ledgerPdfService.GeneratePdf(pdfModel);
    }

    public async Task<LedgerShareActionResult> SendCustomerLedgerEmailAsync(
        int customerId,
        LedgerEmailShareRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ToEmail))
        {
            return new LedgerShareActionResult(false, "Recipient email is required.");
        }

        var shareInfo = await GetCustomerShareInfoAsync(
            customerId,
            request.FromDate,
            request.ToDate,
            cancellationToken);
        if (shareInfo is null)
        {
            return new LedgerShareActionResult(false, "Customer not found.");
        }

        var pdfBytes = await GetCustomerLedgerPdfAsync(
            customerId,
            request.FromDate,
            request.ToDate,
            cancellationToken);
        if (pdfBytes is null)
        {
            return new LedgerShareActionResult(false, "Could not generate ledger PDF.");
        }

        return await SendLedgerEmailAsync(shareInfo, request, pdfBytes, cancellationToken);
    }

    public async Task<LedgerShareActionResult> SendVendorLedgerEmailAsync(
        int vendorId,
        LedgerEmailShareRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ToEmail))
        {
            return new LedgerShareActionResult(false, "Recipient email is required.");
        }

        var shareInfo = await GetVendorShareInfoAsync(
            vendorId,
            request.FromDate,
            request.ToDate,
            cancellationToken);
        if (shareInfo is null)
        {
            return new LedgerShareActionResult(false, "Vendor not found.");
        }

        var pdfBytes = await GetVendorLedgerPdfAsync(
            vendorId,
            request.FromDate,
            request.ToDate,
            cancellationToken);
        if (pdfBytes is null)
        {
            return new LedgerShareActionResult(false, "Could not generate ledger PDF.");
        }

        return await SendLedgerEmailAsync(shareInfo, request, pdfBytes, cancellationToken);
    }

    private async Task<LedgerShareInfoDto?> BuildShareInfoAsync(
        string partyType,
        int partyId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.CompanyId;
        if (!companyId.HasValue)
        {
            return null;
        }

        var companyName = await _unitOfWork.Repository<Company>()
            .Query()
            .Where(c => c.Id == companyId.Value)
            .Select(c => c.CompanyName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Company";

        if (partyType == "customer")
        {
            var customer = await _customerService.GetByIdAsync(partyId, cancellationToken);
            if (customer is null)
            {
                return null;
            }

            var closing = await ResolveCustomerClosingBalanceAsync(partyId, fromDate, toDate, cancellationToken);
            var periodLabel = BuildPeriodLabel(fromDate, toDate);
            var title = fromDate.HasValue && toDate.HasValue ? "Customer Statement" : "Customer Ledger";

            return new LedgerShareInfoDto(
                partyType,
                partyId,
                customer.BuyerName,
                customer.BuyerId,
                customer.Email,
                customer.Mobile,
                customer.Phone,
                companyName,
                periodLabel,
                closing,
                BuildWhatsAppMessage(title, customer.BuyerName, customer.BuyerId, periodLabel, closing, companyName),
                _emailSender.IsConfigured,
                fromDate?.Date,
                toDate?.Date);
        }

        var vendor = await _vendorService.GetByIdAsync(partyId, cancellationToken);
        if (vendor is null)
        {
            return null;
        }

        var vendorClosing = await ResolveVendorClosingBalanceAsync(partyId, fromDate, toDate, cancellationToken);
        var vendorPeriod = BuildPeriodLabel(fromDate, toDate);
        var vendorTitle = fromDate.HasValue && toDate.HasValue ? "Vendor Statement" : "Vendor Ledger";

        return new LedgerShareInfoDto(
            partyType,
            partyId,
            vendor.VendorName,
            vendor.VendorCode,
            vendor.Email,
            null,
            vendor.Phone,
            companyName,
            vendorPeriod,
            vendorClosing,
            BuildWhatsAppMessage(vendorTitle, vendor.VendorName, vendor.VendorCode, vendorPeriod, vendorClosing, companyName),
            _emailSender.IsConfigured,
            fromDate?.Date,
            toDate?.Date);
    }

    private async Task<decimal> ResolveCustomerClosingBalanceAsync(
        int customerId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        if (fromDate.HasValue && toDate.HasValue)
        {
            var statement = await _customerService.GetStatementAsync(
                customerId,
                fromDate.Value,
                toDate.Value,
                cancellationToken);
            return statement?.ClosingBalance ?? 0m;
        }

        var ledger = await _customerService.GetLedgerAsync(customerId, cancellationToken);
        return ledger?.ClosingBalance ?? 0m;
    }

    private async Task<decimal> ResolveVendorClosingBalanceAsync(
        int vendorId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        if (fromDate.HasValue && toDate.HasValue)
        {
            var statement = await _vendorService.GetStatementAsync(
                vendorId,
                fromDate.Value,
                toDate.Value,
                cancellationToken);
            return statement?.ClosingBalance ?? 0m;
        }

        var ledger = await _vendorService.GetLedgerAsync(vendorId, cancellationToken);
        return ledger?.ClosingBalance ?? 0m;
    }

    private async Task<PartyLedgerPdfDto?> BuildCustomerPdfModelAsync(
        int customerId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var companyName = await GetCompanyNameAsync(cancellationToken);

        if (fromDate.HasValue && toDate.HasValue)
        {
            var statement = await _customerService.GetStatementAsync(
                customerId,
                fromDate.Value,
                toDate.Value,
                cancellationToken);
            if (statement is null)
            {
                return null;
            }

            return MapCustomerPdf(
                "Customer Statement",
                companyName,
                statement.Customer.BuyerName,
                statement.Customer.BuyerId,
                statement.Customer.NTN,
                $"Period: {statement.FromDate:dd/MM/yyyy} to {statement.ToDate:dd/MM/yyyy}",
                statement.OpeningBalance,
                statement.ClosingBalance,
                statement.Entries);
        }

        var ledger = await _customerService.GetLedgerAsync(customerId, cancellationToken);
        if (ledger is null)
        {
            return null;
        }

        return MapCustomerPdf(
            "Customer Ledger",
            companyName,
            ledger.Customer.BuyerName,
            ledger.Customer.BuyerId,
            ledger.Customer.NTN,
            $"Full ledger as of {DateTime.Today:dd/MM/yyyy}",
            ledger.Customer.OpeningBalance,
            ledger.ClosingBalance,
            ledger.Entries);
    }

    private async Task<PartyLedgerPdfDto?> BuildVendorPdfModelAsync(
        int vendorId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var companyName = await GetCompanyNameAsync(cancellationToken);

        if (fromDate.HasValue && toDate.HasValue)
        {
            var statement = await _vendorService.GetStatementAsync(
                vendorId,
                fromDate.Value,
                toDate.Value,
                cancellationToken);
            if (statement is null)
            {
                return null;
            }

            return MapVendorPdf(
                "Vendor Statement",
                companyName,
                statement.Vendor.VendorName,
                statement.Vendor.VendorCode,
                statement.Vendor.NTN,
                $"Period: {statement.FromDate:dd/MM/yyyy} to {statement.ToDate:dd/MM/yyyy}",
                statement.OpeningBalance,
                statement.ClosingBalance,
                statement.Entries);
        }

        var ledger = await _vendorService.GetLedgerAsync(vendorId, cancellationToken);
        if (ledger is null)
        {
            return null;
        }

        return MapVendorPdf(
            "Vendor Ledger",
            companyName,
            ledger.Vendor.VendorName,
            ledger.Vendor.VendorCode,
            ledger.Vendor.NTN,
            $"Full ledger as of {DateTime.Today:dd/MM/yyyy}",
            ledger.Vendor.OpeningBalance,
            ledger.ClosingBalance,
            ledger.Entries);
    }

    private async Task<string> GetCompanyNameAsync(CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        return await _unitOfWork.Repository<Company>()
            .Query()
            .Where(c => c.Id == companyId)
            .Select(c => c.CompanyName)
            .FirstAsync(cancellationToken);
    }

    private async Task<LedgerShareActionResult> SendLedgerEmailAsync(
        LedgerShareInfoDto shareInfo,
        LedgerEmailShareRequest request,
        byte[] pdfBytes,
        CancellationToken cancellationToken)
    {
        var title = shareInfo.PeriodLabel?.StartsWith("Period:", StringComparison.Ordinal) == true
            ? (shareInfo.PartyType == "customer" ? "Customer Statement" : "Vendor Statement")
            : (shareInfo.PartyType == "customer" ? "Customer Ledger" : "Vendor Ledger");

        var fileName = SanitizeFileName($"{shareInfo.PartyCode}-{title.Replace(' ', '-')}.pdf");
        var subject = $"{title} - {shareInfo.PartyName} - {shareInfo.CompanyName}";
        var periodText = shareInfo.PeriodLabel ?? "Full ledger";
        var balance = shareInfo.ClosingBalance.ToString("N2", CultureInfo.GetCultureInfo("en-PK"));

        var bodyIntro = string.IsNullOrWhiteSpace(request.Message)
            ? $"Dear {shareInfo.PartyName},<br/><br/>Please find attached your {title.ToLowerInvariant()}."
            : System.Net.WebUtility.HtmlEncode(request.Message).Replace("\n", "<br/>", StringComparison.Ordinal);

        var html = new StringBuilder()
            .Append("<div style=\"font-family:Arial,sans-serif;font-size:14px;\">")
            .Append(bodyIntro)
            .Append("<br/><br/>")
            .Append($"<strong>Party:</strong> {System.Net.WebUtility.HtmlEncode(shareInfo.PartyName)} ({System.Net.WebUtility.HtmlEncode(shareInfo.PartyCode)})<br/>")
            .Append($"<strong>{periodText}</strong><br/>")
            .Append($"<strong>Closing balance:</strong> PKR {balance}")
            .Append("<br/><br/>Regards,<br/>")
            .Append(System.Net.WebUtility.HtmlEncode(shareInfo.CompanyName))
            .Append("</div>")
            .ToString();

        var plain = string.IsNullOrWhiteSpace(request.Message)
            ? $"Dear {shareInfo.PartyName},\n\nPlease find attached your {title}.\n{periodText}\nClosing balance: PKR {balance}"
            : request.Message;

        var result = await _emailSender.SendAsync(
            new EmailMessage(
                request.ToEmail.Trim(),
                subject,
                html,
                plain,
                [new EmailAttachment(fileName, pdfBytes, "application/pdf")]),
            cancellationToken);

        return new LedgerShareActionResult(result.Success, result.Message);
    }

    private static PartyLedgerPdfDto MapCustomerPdf(
        string title,
        string companyName,
        string partyName,
        string partyCode,
        string? ntn,
        string periodLabel,
        decimal opening,
        decimal closing,
        IReadOnlyList<CustomerLedgerEntryDto> entries) =>
        new(
            title,
            partyName,
            partyCode,
            ntn,
            companyName,
            periodLabel,
            opening,
            closing,
            true,
            entries.Select(e => new PartyLedgerPdfLineDto(
                e.Date,
                e.Reference,
                e.Description,
                e.Debit,
                e.Credit,
                e.Balance,
                e.PendingCredit)).ToList());

    private static PartyLedgerPdfDto MapVendorPdf(
        string title,
        string companyName,
        string partyName,
        string partyCode,
        string? ntn,
        string periodLabel,
        decimal opening,
        decimal closing,
        IReadOnlyList<VendorLedgerEntryDto> entries) =>
        new(
            title,
            partyName,
            partyCode,
            ntn,
            companyName,
            periodLabel,
            opening,
            closing,
            false,
            entries.Select(e => new PartyLedgerPdfLineDto(
                e.Date,
                e.Reference,
                e.Description,
                e.Debit,
                e.Credit,
                e.Balance)).ToList());

    private static string? BuildPeriodLabel(DateTime? fromDate, DateTime? toDate) =>
        fromDate.HasValue && toDate.HasValue
            ? $"Period: {fromDate.Value:dd/MM/yyyy} to {toDate.Value:dd/MM/yyyy}"
            : $"Full ledger as of {DateTime.Today:dd/MM/yyyy}";

    private static string BuildWhatsAppMessage(
        string title,
        string partyName,
        string partyCode,
        string? periodLabel,
        decimal closingBalance,
        string companyName)
    {
        var balance = closingBalance.ToString("N2", CultureInfo.GetCultureInfo("en-PK"));
        return
            $"Dear {partyName},\n\n" +
            $"{title}\n" +
            $"Code: {partyCode}\n" +
            $"{periodLabel}\n" +
            $"Closing balance: PKR {balance}\n\n" +
            $"Please find the ledger PDF attached or request it from us.\n\n" +
            $"Regards,\n{companyName}";
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray())
            .Trim('-', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "ledger.pdf" : sanitized;
    }
}
