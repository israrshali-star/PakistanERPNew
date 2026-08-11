using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common;
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
    private readonly ICustomerReceiptAttachmentService _receiptAttachmentService;

    public LedgerShareService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICustomerService customerService,
        IVendorService vendorService,
        ILedgerPdfService ledgerPdfService,
        IEmailSender emailSender,
        ICustomerReceiptAttachmentService receiptAttachmentService)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _customerService = customerService;
        _vendorService = vendorService;
        _ledgerPdfService = ledgerPdfService;
        _emailSender = emailSender;
        _receiptAttachmentService = receiptAttachmentService;
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
        bool useUrdu = false,
        CancellationToken cancellationToken = default)
    {
        useUrdu = ResolveUseUrdu(useUrdu);
        var pdfModel = await BuildCustomerPdfModelAsync(customerId, fromDate, toDate, useUrdu, cancellationToken);
        return pdfModel is null ? null : _ledgerPdfService.GeneratePdf(pdfModel);
    }

    public async Task<byte[]?> GetVendorLedgerPdfAsync(
        int vendorId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        bool useUrdu = false,
        CancellationToken cancellationToken = default)
    {
        useUrdu = ResolveUseUrdu(useUrdu);
        var pdfModel = await BuildVendorPdfModelAsync(vendorId, fromDate, toDate, useUrdu, cancellationToken);
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

        var useUrdu = ResolveUseUrdu(request.UseUrdu);
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
            useUrdu,
            cancellationToken);
        if (pdfBytes is null)
        {
            return new LedgerShareActionResult(false, "Could not generate ledger PDF.");
        }

        var receiptDocs = await CollectCustomerReceiptEmailAttachmentsAsync(
            customerId,
            request.FromDate,
            request.ToDate,
            cancellationToken);

        return await SendLedgerEmailAsync(
            shareInfo,
            request,
            pdfBytes,
            useUrdu,
            receiptDocs,
            cancellationToken);
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

        var useUrdu = ResolveUseUrdu(request.UseUrdu);
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
            useUrdu,
            cancellationToken);
        if (pdfBytes is null)
        {
            return new LedgerShareActionResult(false, "Could not generate ledger PDF.");
        }

        return await SendLedgerEmailAsync(shareInfo, request, pdfBytes, useUrdu, null, cancellationToken);
    }

    private bool ResolveUseUrdu(bool requested) =>
        requested && TradeInvoiceLayout.SupportsUrduLedger(_currentCompany.GetRequiredCompanyId());

    private async Task<IReadOnlyList<EmailAttachment>> CollectCustomerReceiptEmailAttachmentsAsync(
        int customerId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CustomerLedgerEntryDto> entries;
        if (fromDate.HasValue && toDate.HasValue)
        {
            var statement = await _customerService.GetStatementAsync(
                customerId,
                fromDate.Value,
                toDate.Value,
                cancellationToken);
            entries = statement?.Entries ?? Array.Empty<CustomerLedgerEntryDto>();
        }
        else
        {
            var ledger = await _customerService.GetLedgerAsync(customerId, cancellationToken);
            entries = ledger?.Entries ?? Array.Empty<CustomerLedgerEntryDto>();
        }

        var attachmentIds = entries
            .Where(e => e.Attachments is { Count: > 0 })
            .SelectMany(e => e.Attachments!)
            .Select(a => a.Id)
            .Distinct()
            .ToList();

        if (attachmentIds.Count == 0)
        {
            return Array.Empty<EmailAttachment>();
        }

        var emailAttachments = new List<EmailAttachment>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attachmentId in attachmentIds)
        {
            var file = await _receiptAttachmentService.DownloadAsync(attachmentId, cancellationToken);
            if (file is null)
            {
                continue;
            }

            var uniqueName = file.FileName;
            var stem = Path.GetFileNameWithoutExtension(file.FileName);
            var ext = Path.GetExtension(file.FileName);
            var suffix = 1;
            while (!usedNames.Add(uniqueName))
            {
                uniqueName = $"{stem}-{suffix++}{ext}";
            }

            emailAttachments.Add(new EmailAttachment(uniqueName, file.Content, file.ContentType));
        }

        return emailAttachments;
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

        var supportsUrdu = TradeInvoiceLayout.SupportsUrduLedger(companyId.Value);
        var isStatement = fromDate.HasValue && toDate.HasValue;

        if (partyType == "customer")
        {
            var customer = await _customerService.GetByIdAsync(partyId, cancellationToken);
            if (customer is null)
            {
                return null;
            }

            var closing = await ResolveCustomerClosingBalanceAsync(partyId, fromDate, toDate, cancellationToken);
            var en = LedgerPdfLabels.English;
            var ur = LedgerPdfLabels.Urdu;
            var periodEn = en.BuildPeriodLabel(fromDate, toDate);
            var periodUr = ur.BuildPeriodLabel(fromDate, toDate);
            var titleEn = en.TitleFor(partyType, isStatement);
            var titleUr = ur.TitleFor(partyType, isStatement);

            var urduPartyName = RomanUrduTransliterator.ResolveDisplayName(
                customer.BuyerName,
                customer.BuyerNameUrdu,
                useUrdu: true);
            return new LedgerShareInfoDto(
                partyType,
                partyId,
                customer.BuyerName,
                customer.BuyerId,
                customer.Email,
                customer.Mobile,
                customer.Phone,
                companyName,
                periodEn,
                closing,
                BuildWhatsAppMessage(en, titleEn, customer.BuyerName, customer.BuyerId, periodEn, closing, companyName),
                _emailSender.IsConfigured,
                fromDate?.Date,
                toDate?.Date,
                supportsUrdu,
                supportsUrdu
                    ? BuildWhatsAppMessage(ur, titleUr, urduPartyName, customer.BuyerId, periodUr, closing, companyName)
                    : null,
                customer.BuyerNameUrdu);
        }

        var vendor = await _vendorService.GetByIdAsync(partyId, cancellationToken);
        if (vendor is null)
        {
            return null;
        }

        var vendorClosing = await ResolveVendorClosingBalanceAsync(partyId, fromDate, toDate, cancellationToken);
        var labelsEn = LedgerPdfLabels.English;
        var labelsUr = LedgerPdfLabels.Urdu;
        var vendorPeriodEn = labelsEn.BuildPeriodLabel(fromDate, toDate);
        var vendorPeriodUr = labelsUr.BuildPeriodLabel(fromDate, toDate);
        var vendorTitleEn = labelsEn.TitleFor(partyType, isStatement);
        var vendorTitleUr = labelsUr.TitleFor(partyType, isStatement);
        var urduVendorName = RomanUrduTransliterator.ResolveDisplayName(
            vendor.VendorName,
            vendor.VendorNameUrdu,
            useUrdu: true);

        return new LedgerShareInfoDto(
            partyType,
            partyId,
            vendor.VendorName,
            vendor.VendorCode,
            vendor.Email,
            null,
            vendor.Phone,
            companyName,
            vendorPeriodEn,
            vendorClosing,
            BuildWhatsAppMessage(labelsEn, vendorTitleEn, vendor.VendorName, vendor.VendorCode, vendorPeriodEn, vendorClosing, companyName),
            _emailSender.IsConfigured,
            fromDate?.Date,
            toDate?.Date,
            supportsUrdu,
            supportsUrdu
                ? BuildWhatsAppMessage(labelsUr, vendorTitleUr, urduVendorName, vendor.VendorCode, vendorPeriodUr, vendorClosing, companyName)
                : null,
            vendor.VendorNameUrdu);
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
        bool useUrdu,
        CancellationToken cancellationToken)
    {
        var companyName = await GetCompanyNameAsync(cancellationToken);
        var labels = LedgerPdfLabels.For(useUrdu);
        var isStatement = fromDate.HasValue && toDate.HasValue;

        if (isStatement)
        {
            var statement = await _customerService.GetStatementAsync(
                customerId,
                fromDate!.Value,
                toDate!.Value,
                cancellationToken);
            if (statement is null)
            {
                return null;
            }

            return MapCustomerPdf(
                labels.CustomerStatement,
                companyName,
                ResolvePartyName(statement.Customer.BuyerName, statement.Customer.BuyerNameUrdu, useUrdu),
                statement.Customer.BuyerId,
                statement.Customer.NTN,
                labels.BuildPeriodLabel(statement.FromDate, statement.ToDate),
                statement.OpeningBalance,
                statement.ClosingBalance,
                statement.Entries,
                useUrdu);
        }

        var ledger = await _customerService.GetLedgerAsync(customerId, cancellationToken);
        if (ledger is null)
        {
            return null;
        }

        return MapCustomerPdf(
            labels.CustomerLedger,
            companyName,
            ResolvePartyName(ledger.Customer.BuyerName, ledger.Customer.BuyerNameUrdu, useUrdu),
            ledger.Customer.BuyerId,
            ledger.Customer.NTN,
            labels.BuildPeriodLabel(null, null),
            ledger.Customer.OpeningBalance,
            ledger.ClosingBalance,
            ledger.Entries,
            useUrdu);
    }

    private async Task<PartyLedgerPdfDto?> BuildVendorPdfModelAsync(
        int vendorId,
        DateTime? fromDate,
        DateTime? toDate,
        bool useUrdu,
        CancellationToken cancellationToken)
    {
        var companyName = await GetCompanyNameAsync(cancellationToken);
        var labels = LedgerPdfLabels.For(useUrdu);
        var isStatement = fromDate.HasValue && toDate.HasValue;

        if (isStatement)
        {
            var statement = await _vendorService.GetStatementAsync(
                vendorId,
                fromDate!.Value,
                toDate!.Value,
                cancellationToken);
            if (statement is null)
            {
                return null;
            }

            return MapVendorPdf(
                labels.VendorStatement,
                companyName,
                ResolvePartyName(statement.Vendor.VendorName, statement.Vendor.VendorNameUrdu, useUrdu),
                statement.Vendor.VendorCode,
                statement.Vendor.NTN,
                labels.BuildPeriodLabel(statement.FromDate, statement.ToDate),
                statement.OpeningBalance,
                statement.ClosingBalance,
                statement.Entries,
                useUrdu);
        }

        var ledger = await _vendorService.GetLedgerAsync(vendorId, cancellationToken);
        if (ledger is null)
        {
            return null;
        }

        return MapVendorPdf(
            labels.VendorLedger,
            companyName,
            ResolvePartyName(ledger.Vendor.VendorName, ledger.Vendor.VendorNameUrdu, useUrdu),
            ledger.Vendor.VendorCode,
            ledger.Vendor.NTN,
            labels.BuildPeriodLabel(null, null),
            ledger.Vendor.OpeningBalance,
            ledger.ClosingBalance,
            ledger.Entries,
            useUrdu);
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
        bool useUrdu,
        IReadOnlyList<EmailAttachment>? extraAttachments,
        CancellationToken cancellationToken)
    {
        var labels = LedgerPdfLabels.For(useUrdu);
        var isStatement = shareInfo.FromDate.HasValue && shareInfo.ToDate.HasValue;
        var title = labels.TitleFor(shareInfo.PartyType, isStatement);
        var periodText = labels.BuildPeriodLabel(shareInfo.FromDate, shareInfo.ToDate);
        var partyName = RomanUrduTransliterator.ResolveDisplayName(
            shareInfo.PartyName,
            shareInfo.PartyNameUrdu,
            useUrdu);
        var fileName = SanitizeFileName(
            $"{shareInfo.PartyCode}-{title.Replace(' ', '-')}{(useUrdu ? "-ur" : string.Empty)}.pdf");
        var subject = $"{title} - {partyName} - {shareInfo.CompanyName}";
        var balance = shareInfo.ClosingBalance.ToString("N2", CultureInfo.GetCultureInfo("en-PK"));

        string bodyIntro;
        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            bodyIntro = System.Net.WebUtility.HtmlEncode(request.Message)
                .Replace("\n", "<br/>", StringComparison.Ordinal);
        }
        else if (useUrdu)
        {
            bodyIntro =
                $"{labels.Dear} {System.Net.WebUtility.HtmlEncode(partyName)},<br/><br/>" +
                $"{labels.PleaseFindAttached}۔";
        }
        else
        {
            bodyIntro =
                $"Dear {System.Net.WebUtility.HtmlEncode(partyName)},<br/><br/>" +
                $"Please find attached your {title.ToLowerInvariant()}.";
        }

        var docsNote = extraAttachments is { Count: > 0 }
            ? (useUrdu
                ? "<br/><br/>چیک / بینک ٹرانسفر دستاویزات بھی منسلک ہیں۔"
                : "<br/><br/>Cheque / bank transfer documents are also attached for reconciliation.")
            : string.Empty;

        var html = new StringBuilder()
            .Append(useUrdu
                ? "<div style=\"font-family:'Urdu Typesetting','Nirmala UI',Arial,sans-serif;font-size:14px;direction:rtl;text-align:right;\">"
                : "<div style=\"font-family:Arial,sans-serif;font-size:14px;\">")
            .Append(bodyIntro)
            .Append("<br/><br/>")
            .Append($"<strong>{labels.Party}:</strong> {System.Net.WebUtility.HtmlEncode(partyName)} ({System.Net.WebUtility.HtmlEncode(shareInfo.PartyCode)})<br/>")
            .Append($"<strong>{periodText}</strong><br/>")
            .Append($"<strong>{labels.ClosingBalance}:</strong> PKR {balance}")
            .Append(docsNote)
            .Append($"<br/><br/>{labels.Regards}<br/>")
            .Append(System.Net.WebUtility.HtmlEncode(shareInfo.CompanyName))
            .Append("</div>")
            .ToString();

        var plain = string.IsNullOrWhiteSpace(request.Message)
            ? (useUrdu
                ? $"{labels.Dear} {partyName},\n\n{labels.PleaseFindAttached}.\n{periodText}\n{labels.ClosingBalance}: PKR {balance}"
                : $"Dear {partyName},\n\nPlease find attached your {title}.\n{periodText}\nClosing balance: PKR {balance}")
            : request.Message;

        if (extraAttachments is { Count: > 0 } && string.IsNullOrWhiteSpace(request.Message))
        {
            plain += useUrdu
                ? "\n\nچیک / بینک ٹرانسفر دستاویزات بھی منسلک ہیں۔"
                : "\n\nCheque / bank transfer documents are also attached for reconciliation.";
        }

        var attachments = new List<EmailAttachment>
        {
            new(fileName, pdfBytes, "application/pdf")
        };
        if (extraAttachments is { Count: > 0 })
        {
            attachments.AddRange(extraAttachments);
        }

        var result = await _emailSender.SendAsync(
            new EmailMessage(
                request.ToEmail.Trim(),
                subject,
                html,
                plain,
                attachments),
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
        IReadOnlyList<CustomerLedgerEntryDto> entries,
        bool useUrdu) =>
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
            entries.Select(e =>
            {
                var description = e.Description;
                if (e.Attachments is { Count: > 0 })
                {
                    var names = string.Join(", ", e.Attachments.Select(a => a.FileName));
                    description = $"{description} [Docs: {names}]";
                }

                return new PartyLedgerPdfLineDto(
                    e.Date,
                    e.Reference,
                    description,
                    e.Debit,
                    e.Credit,
                    e.Balance,
                    e.PendingCredit);
            }).ToList(),
            useUrdu);

    private static PartyLedgerPdfDto MapVendorPdf(
        string title,
        string companyName,
        string partyName,
        string partyCode,
        string? ntn,
        string periodLabel,
        decimal opening,
        decimal closing,
        IReadOnlyList<VendorLedgerEntryDto> entries,
        bool useUrdu) =>
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
                e.Balance)).ToList(),
            useUrdu);

    private static string ResolvePartyName(string partyName, string? partyNameUrdu, bool useUrdu) =>
        RomanUrduTransliterator.ResolveDisplayName(partyName, partyNameUrdu, useUrdu);

    private static string BuildWhatsAppMessage(
        LedgerPdfLabels labels,
        string title,
        string partyName,
        string partyCode,
        string? periodLabel,
        decimal closingBalance,
        string companyName)
    {
        var balance = closingBalance.ToString("N2", CultureInfo.GetCultureInfo("en-PK"));
        return
            $"{labels.Dear} {partyName},\n\n" +
            $"{title}\n" +
            $"{labels.Code}: {partyCode}\n" +
            $"{periodLabel}\n" +
            $"{labels.ClosingBalance}: PKR {balance}\n\n" +
            $"{labels.WhatsAppAttachHint}\n\n" +
            $"{labels.Regards},\n{companyName}";
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
