using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class InvoiceShareService : IInvoiceShareService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ISalesInvoiceService _salesInvoiceService;
    private readonly ISalesInvoicePdfService _salesInvoicePdfService;
    private readonly ITradeInvoicePdfService _tradeInvoicePdfService;
    private readonly IDeliveryChallanPdfService _deliveryChallanPdfService;
    private readonly IEmailSender _emailSender;

    public InvoiceShareService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ISalesInvoiceService salesInvoiceService,
        ISalesInvoicePdfService salesInvoicePdfService,
        ITradeInvoicePdfService tradeInvoicePdfService,
        IDeliveryChallanPdfService deliveryChallanPdfService,
        IEmailSender emailSender)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _salesInvoiceService = salesInvoiceService;
        _salesInvoicePdfService = salesInvoicePdfService;
        _tradeInvoicePdfService = tradeInvoicePdfService;
        _deliveryChallanPdfService = deliveryChallanPdfService;
        _emailSender = emailSender;
    }

    public async Task<SalesInvoiceShareInfoDto?> GetShareInfoAsync(
        int invoiceId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.CompanyId;
        if (!companyId.HasValue)
        {
            return null;
        }

        var invoice = await _unitOfWork.Repository<Domain.Entities.SalesInvoice>()
            .Query()
            .Where(i => i.Id == invoiceId && i.CompanyId == companyId.Value)
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.FbrInvoiceNumber,
                i.InvoiceDate,
                i.NetTotal,
                i.Status,
                i.FbrSubmittedAt,
                SellerCompanyName = i.Company.CompanyName,
                GodownEmail = i.Company.GodownEmail,
                i.Customer.BuyerName,
                i.Customer.Email,
                i.Customer.Mobile,
                i.Customer.Phone,
                LineCount = i.Lines.Count
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var canShare = CanShareInvoice(companyId.Value, invoice.Status, invoice.FbrSubmittedAt);
        var canEmailChallan = CanEmailDeliveryChallan(invoice.Status, invoice.LineCount);
        var message = BuildWhatsAppMessage(
            invoice.BuyerName,
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            invoice.NetTotal,
            invoice.SellerCompanyName);

        return new SalesInvoiceShareInfoDto(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.FbrInvoiceNumber,
            invoice.BuyerName,
            invoice.Email,
            invoice.Mobile,
            invoice.Phone,
            invoice.SellerCompanyName,
            invoice.InvoiceDate,
            invoice.NetTotal,
            canShare,
            message,
            _emailSender.IsConfigured,
            invoice.GodownEmail,
            canEmailChallan);
    }

    public async Task<SalesInvoiceShareActionResult> SendEmailAsync(
        int invoiceId,
        SalesInvoiceEmailShareRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ToEmail))
        {
            return new SalesInvoiceShareActionResult(false, "Recipient email is required.");
        }

        var shareInfo = await GetShareInfoAsync(invoiceId, cancellationToken);
        if (shareInfo is null)
        {
            return new SalesInvoiceShareActionResult(false, "Invoice not found.");
        }

        if (!shareInfo.CanShare)
        {
            return new SalesInvoiceShareActionResult(
                false,
                "Invoice must be finalized before it can be shared.");
        }

        var pdf = await GenerateInvoicePdfAsync(invoiceId, cancellationToken);
        if (pdf is null)
        {
            return new SalesInvoiceShareActionResult(false, "Could not generate invoice PDF.");
        }

        var (pdfBytes, fileName) = pdf.Value;
        var displayNumber = shareInfo.FbrInvoiceNumber ?? shareInfo.InvoiceNumber;
        var subject = $"Invoice {displayNumber} - {shareInfo.SellerCompanyName ?? "Invoice"}";
        var bodyIntro = string.IsNullOrWhiteSpace(request.Message)
            ? $"Dear {shareInfo.CustomerName},<br/><br/>Please find attached invoice <strong>{shareInfo.InvoiceNumber}</strong> dated {shareInfo.InvoiceDate:dd/MM/yyyy}."
            : System.Net.WebUtility.HtmlEncode(request.Message).Replace("\n", "<br/>", StringComparison.Ordinal);

        var html = new StringBuilder()
            .Append("<div style=\"font-family:Arial,sans-serif;font-size:14px;\">")
            .Append(bodyIntro)
            .Append("<br/><br/>")
            .Append($"<strong>Amount:</strong> PKR {shareInfo.NetTotal.ToString("N2", CultureInfo.GetCultureInfo("en-PK"))}<br/>")
            .Append($"<strong>FBR / Invoice #:</strong> {System.Net.WebUtility.HtmlEncode(displayNumber)}")
            .Append("<br/><br/>Regards,<br/>")
            .Append(System.Net.WebUtility.HtmlEncode(shareInfo.SellerCompanyName ?? "Pakistan Accounting ERP"))
            .Append("</div>")
            .ToString();

        var plain = string.IsNullOrWhiteSpace(request.Message)
            ? $"Dear {shareInfo.CustomerName},\n\nPlease find attached invoice {shareInfo.InvoiceNumber} dated {shareInfo.InvoiceDate:dd/MM/yyyy}.\nAmount: PKR {shareInfo.NetTotal:N2}\nFBR / Invoice #: {displayNumber}"
            : request.Message;

        var result = await _emailSender.SendAsync(
            new EmailMessage(
                request.ToEmail.Trim(),
                subject,
                html,
                plain,
                [new EmailAttachment(fileName, pdfBytes, "application/pdf")]),
            cancellationToken);

        return new SalesInvoiceShareActionResult(result.Success, result.Message);
    }

    public async Task<SalesInvoiceShareActionResult> SendDeliveryChallanEmailAsync(
        int invoiceId,
        SalesInvoiceChallanEmailShareRequest request,
        CancellationToken cancellationToken = default)
    {
        var shareInfo = await GetShareInfoAsync(invoiceId, cancellationToken);
        if (shareInfo is null)
        {
            return new SalesInvoiceShareActionResult(false, "Invoice not found.");
        }

        if (!shareInfo.CanEmailChallan)
        {
            return new SalesInvoiceShareActionResult(
                false,
                "Delivery challan can only be emailed for posted invoices with line items.");
        }

        var toEmail = string.IsNullOrWhiteSpace(request.ToEmail)
            ? shareInfo.GodownEmail
            : request.ToEmail.Trim();

        if (string.IsNullOrWhiteSpace(toEmail))
        {
            return new SalesInvoiceShareActionResult(
                false,
                "Godown email is not configured. Set it in Company & FBR Settings.");
        }

        var challanData = await _salesInvoiceService.GetDeliveryChallanDataAsync(invoiceId, cancellationToken);
        if (challanData is null || challanData.Lines.Count == 0)
        {
            return new SalesInvoiceShareActionResult(false, "Could not generate delivery challan.");
        }

        var pdfBytes = _deliveryChallanPdfService.GeneratePdf(challanData);
        var fileName = $"DC-{challanData.InvoiceNumber}.pdf".Replace('/', '-');
        var totalCartons = challanData.Lines
            .Where(l => !l.IsTransportation)
            .Sum(l => l.Cartons);
        var totalQty = challanData.Lines
            .Where(l => !l.IsTransportation)
            .Sum(l => l.Quantity);
        var subject = $"Delivery Challan {challanData.InvoiceNumber} - {challanData.BuyerName}";

        var bodyIntro = string.IsNullOrWhiteSpace(request.Message)
            ? "Please find attached delivery challan for dispatch."
            : System.Net.WebUtility.HtmlEncode(request.Message).Replace("\n", "<br/>", StringComparison.Ordinal);

        var html = new StringBuilder()
            .Append("<div style=\"font-family:Arial,sans-serif;font-size:14px;\">")
            .Append(bodyIntro)
            .Append("<br/><br/>")
            .Append($"<strong>Invoice #:</strong> {System.Net.WebUtility.HtmlEncode(challanData.InvoiceNumber)}<br/>")
            .Append($"<strong>Date:</strong> {challanData.InvoiceDate:dd/MM/yyyy}<br/>")
            .Append($"<strong>Customer:</strong> {System.Net.WebUtility.HtmlEncode(challanData.BuyerName)}<br/>")
            .Append($"<strong>Total cartons:</strong> {totalCartons.ToString("N2", CultureInfo.InvariantCulture)}<br/>")
            .Append($"<strong>Total quantity:</strong> {totalQty.ToString("N2", CultureInfo.InvariantCulture)}")
            .Append("<br/><br/>Regards,<br/>")
            .Append(System.Net.WebUtility.HtmlEncode(challanData.SellerName))
            .Append("</div>")
            .ToString();

        var plain = string.IsNullOrWhiteSpace(request.Message)
            ? $"Delivery challan for invoice {challanData.InvoiceNumber} dated {challanData.InvoiceDate:dd/MM/yyyy}.\nCustomer: {challanData.BuyerName}\nCartons: {totalCartons:N2}\nQuantity: {totalQty:N2}"
            : request.Message;

        var result = await _emailSender.SendAsync(
            new EmailMessage(
                toEmail,
                subject,
                html,
                plain,
                [new EmailAttachment(fileName, pdfBytes, "application/pdf")]),
            cancellationToken);

        return new SalesInvoiceShareActionResult(result.Success, result.Message);
    }

    private async Task<(byte[] Bytes, string FileName)?> GenerateInvoicePdfAsync(
        int invoiceId,
        CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        if (companyId == TradeInvoiceLayout.TradeInvoiceCompanyId)
        {
            var tradeData = await _salesInvoiceService.GetTradeInvoicePrintDataAsync(invoiceId, cancellationToken);
            if (tradeData is null)
            {
                return null;
            }

            var tradeFileName = $"{tradeData.InvoiceNumber}.pdf".Replace('/', '-');
            return (_tradeInvoicePdfService.GeneratePdf(tradeData), tradeFileName);
        }

        var printData = await _salesInvoiceService.GetPrintDataAsync(invoiceId, cancellationToken);
        if (printData is null)
        {
            return null;
        }

        var fileName = $"{printData.InvoiceNumber}.pdf".Replace('/', '-');
        return (_salesInvoicePdfService.GeneratePdf(printData), fileName);
    }

    private static bool CanShareInvoice(int companyId, InvoiceStatus status, DateTime? fbrSubmittedAt) =>
        status == InvoiceStatus.Posted
        && (fbrSubmittedAt.HasValue
            || companyId == TradeInvoiceLayout.TradeInvoiceCompanyId);

    private static bool CanEmailDeliveryChallan(InvoiceStatus status, int lineCount) =>
        status == InvoiceStatus.Posted && lineCount > 0;

    private static string BuildWhatsAppMessage(
        string customerName,
        string invoiceNumber,
        DateTime invoiceDate,
        decimal netTotal,
        string? sellerName)
    {
        var amount = netTotal.ToString("N2", CultureInfo.GetCultureInfo("en-PK"));
        return
            $"Dear {customerName},\n\n" +
            $"Invoice: {invoiceNumber}\n" +
            $"Date: {invoiceDate:dd/MM/yyyy}\n" +
            $"Amount: PKR {amount}\n\n" +
            $"Please find the invoice PDF attached or request it from us.\n\n" +
            $"Regards,\n{sellerName ?? "Accounts Department"}";
    }
}
