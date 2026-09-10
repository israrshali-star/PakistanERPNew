using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class CustomerReceiptShareService : ICustomerReceiptShareService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICustomerReceiptPdfService _receiptPdfService;
    private readonly ICustomerReceiptInvoiceAllocationService _allocationService;

    public CustomerReceiptShareService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICustomerReceiptPdfService receiptPdfService,
        ICustomerReceiptInvoiceAllocationService allocationService)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _receiptPdfService = receiptPdfService;
        _allocationService = allocationService;
    }

    public async Task<CustomerReceiptShareInfoDto?> GetShareInfoAsync(
        int receiptId,
        CancellationToken cancellationToken = default)
    {
        var model = await LoadReceiptShareModelAsync(receiptId, cancellationToken);
        if (model is null)
        {
            return null;
        }

        var supportsUrdu = TradeInvoiceLayout.SupportsUrduLedger(model.CompanyId);
        string? messageUrdu = null;
        if (supportsUrdu)
        {
            var urduName = RomanUrduTransliterator.ResolveDisplayName(
                model.CustomerName,
                model.CustomerNameUrdu,
                useUrdu: true);
            messageUrdu = BuildWhatsAppMessage(model with
            {
                CustomerName = urduName
            }, useUrdu: true);
        }

        return new CustomerReceiptShareInfoDto(
            model.ReceiptId,
            model.ReceiptNumber,
            model.CustomerName,
            model.CustomerCode,
            model.ReceiptDate,
            model.Amount,
            model.PaymentMethodLabel,
            model.CustomerEmail,
            model.CustomerMobile,
            model.CustomerPhone,
            model.CompanyName,
            BuildWhatsAppMessage(model, useUrdu: false),
            supportsUrdu,
            messageUrdu,
            model.CustomerNameUrdu);
    }

    public async Task<byte[]?> GetReceiptPdfAsync(
        int receiptId,
        bool useUrdu = false,
        CancellationToken cancellationToken = default)
    {
        var model = await LoadReceiptShareModelAsync(receiptId, cancellationToken);
        if (model is null)
        {
            return null;
        }

        useUrdu = useUrdu && TradeInvoiceLayout.SupportsUrduLedger(model.CompanyId);
        CustomerReceiptInvoiceAllocationDto? allocation = null;
        if (TradeInvoiceLayout.ShowsCustomerReceiptInvoiceAllocation(model.CompanyId))
        {
            allocation = await _allocationService.GetAllocationAsync(
                model.CustomerId,
                model.ReceiptDate,
                model.Amount,
                model.ReceiptId,
                cancellationToken);
        }

        return _receiptPdfService.GeneratePdf(MapPdfDto(model, useUrdu, allocation));
    }

    private async Task<ReceiptShareModel?> LoadReceiptShareModelAsync(
        int receiptId,
        CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.CompanyId;
        if (!companyId.HasValue)
        {
            return null;
        }

        var today = DateTime.Today;
        var row = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => r.Id == receiptId && r.CompanyId == companyId.Value)
            .Select(r => new
            {
                r.Id,
                r.CompanyId,
                r.ReceiptNumber,
                r.CustomerId,
                r.ReceiptDate,
                r.Amount,
                r.PaymentMethod,
                r.ChequeBankType,
                r.Status,
                r.IsDeposited,
                r.ClearedAt,
                r.ChequeDate,
                r.ChequeNumber,
                r.Notes,
                CustomerName = r.Customer.BuyerName,
                CustomerNameUrdu = r.Customer.BuyerNameUrdu,
                CustomerCode = r.Customer.BuyerId,
                CustomerEmail = r.Customer.Email,
                CustomerMobile = r.Customer.Mobile,
                CustomerPhone = r.Customer.Phone,
                BankName = r.Bank != null ? r.Bank.BankName : null,
                CompanyName = r.Company.CompanyName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new ReceiptShareModel(
            row.Id,
            row.CompanyId,
            row.ReceiptNumber,
            row.CustomerId,
            row.CustomerName,
            row.CustomerNameUrdu,
            row.CustomerCode,
            row.ReceiptDate,
            row.Amount,
            GetPaymentMethodLabel(row.PaymentMethod, row.ChequeBankType, row.BankName),
            row.BankName,
            row.ChequeNumber,
            row.ChequeDate,
            row.Notes,
            GetStatusLabel(
                row.PaymentMethod,
                row.ChequeBankType,
                row.Status,
                row.IsDeposited,
                row.ClearedAt,
                row.ChequeDate,
                today),
            row.CustomerEmail,
            row.CustomerMobile,
            row.CustomerPhone,
            row.CompanyName);
    }

    private static CustomerReceiptPdfDto MapPdfDto(
        ReceiptShareModel model,
        bool useUrdu,
        CustomerReceiptInvoiceAllocationDto? allocation)
    {
        var customerName = RomanUrduTransliterator.ResolveDisplayName(
            model.CustomerName,
            model.CustomerNameUrdu,
            useUrdu);

        var remaining = allocation is null
            ? (decimal?)null
            : Math.Max(0m, allocation.RemainingBalance);

        return new CustomerReceiptPdfDto(
            model.CompanyName,
            model.ReceiptNumber,
            customerName,
            model.CustomerCode,
            model.ReceiptDate,
            model.Amount,
            allocation?.OutstandingBefore ?? model.Amount,
            model.PaymentMethodLabel,
            model.BankName,
            model.ChequeNumber,
            model.ChequeDate,
            model.Notes,
            model.StatusLabel,
            useUrdu,
            remaining,
            allocation?.Invoices);
    }

    private static string BuildWhatsAppMessage(ReceiptShareModel model, bool useUrdu)
    {
        var labels = CustomerReceiptPdfLabels.For(useUrdu);
        var amount = model.Amount.ToString("N2", CultureInfo.GetCultureInfo("en-PK"));
        var message =
            $"{labels.Dear} {model.CustomerName},\n\n" +
            $"{labels.Receipt}: {model.ReceiptNumber}\n" +
            $"{labels.Customer}: {model.CustomerCode}\n" +
            $"{labels.Date}: {model.ReceiptDate:dd/MM/yyyy}\n" +
            $"{labels.Amount}: PKR {amount}\n" +
            $"{labels.Payment}: {model.PaymentMethodLabel}\n";

        if (!string.IsNullOrWhiteSpace(model.ChequeNumber))
        {
            message += $"{labels.ChequeRef}: {model.ChequeNumber}";
            if (model.ChequeDate.HasValue)
            {
                message += $" · {labels.Date}: {model.ChequeDate.Value:dd/MM/yyyy}";
            }

            message += '\n';
        }
        else if (!string.IsNullOrWhiteSpace(model.Notes)
                 && model.PaymentMethodLabel.Contains("Bank Transfer", StringComparison.OrdinalIgnoreCase))
        {
            message += $"{labels.RefNo}: {model.Notes.Trim()}\n";
        }

        return message +
               $"\n{labels.WhatsAppAttachHint}\n\n" +
               $"{labels.Regards},\n{model.CompanyName}";
    }

    private static string GetPaymentMethodLabel(
        PaymentMethod paymentMethod,
        ChequeBankType? chequeBankType,
        string? bankName)
    {
        var label = paymentMethod switch
        {
            PaymentMethod.Cheque when chequeBankType == ChequeBankType.SameBank => "Cheque (Same Bank)",
            PaymentMethod.Cheque when chequeBankType == ChequeBankType.OtherBank => "Cheque (Other Bank)",
            PaymentMethod.Cheque => "Cheque",
            PaymentMethod.BankTransfer => "Bank Transfer",
            _ => "Cash"
        };

        if (!string.IsNullOrWhiteSpace(bankName)
            && paymentMethod is PaymentMethod.Cash or PaymentMethod.BankTransfer)
        {
            return $"{label} — {bankName.Trim()}";
        }

        return label;
    }

    private static string GetStatusLabel(
        PaymentMethod paymentMethod,
        ChequeBankType? chequeBankType,
        CustomerReceiptStatus status,
        bool isDeposited,
        DateTime? clearedAt,
        DateTime? chequeDate,
        DateTime today)
    {
        if (paymentMethod != PaymentMethod.Cheque)
        {
            return "Cleared";
        }

        if (status == CustomerReceiptStatus.Returned)
        {
            return "Returned (Not Cleared)";
        }

        if (chequeBankType == ChequeBankType.SameBank || clearedAt.HasValue)
        {
            return "Cleared";
        }

        if (isDeposited)
        {
            return "Deposited (Awaiting Approval)";
        }

        if (chequeDate.HasValue && chequeDate.Value.Date > today)
        {
            return "Post-dated (Undeposited)";
        }

        return "Undeposited";
    }

    private sealed record ReceiptShareModel(
        int ReceiptId,
        int CompanyId,
        string ReceiptNumber,
        int CustomerId,
        string CustomerName,
        string? CustomerNameUrdu,
        string CustomerCode,
        DateTime ReceiptDate,
        decimal Amount,
        string PaymentMethodLabel,
        string? BankName,
        string? ChequeNumber,
        DateTime? ChequeDate,
        string? Notes,
        string StatusLabel,
        string? CustomerEmail,
        string? CustomerMobile,
        string? CustomerPhone,
        string CompanyName);
}
