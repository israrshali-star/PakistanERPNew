using System.Globalization;
using Microsoft.EntityFrameworkCore;
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

    public CustomerReceiptShareService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICustomerReceiptPdfService receiptPdfService)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _receiptPdfService = receiptPdfService;
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
            BuildWhatsAppMessage(model));
    }

    public async Task<byte[]?> GetReceiptPdfAsync(int receiptId, CancellationToken cancellationToken = default)
    {
        var model = await LoadReceiptShareModelAsync(receiptId, cancellationToken);
        if (model is null)
        {
            return null;
        }

        return _receiptPdfService.GeneratePdf(MapPdfDto(model));
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
                r.ReceiptNumber,
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
            row.ReceiptNumber,
            row.CustomerName,
            row.CustomerCode,
            row.ReceiptDate,
            row.Amount,
            GetPaymentMethodLabel(row.PaymentMethod, row.ChequeBankType),
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

    private static CustomerReceiptPdfDto MapPdfDto(ReceiptShareModel model) =>
        new(
            model.CompanyName,
            model.ReceiptNumber,
            model.CustomerName,
            model.CustomerCode,
            model.ReceiptDate,
            model.Amount,
            model.Amount,
            model.PaymentMethodLabel,
            model.BankName,
            model.ChequeNumber,
            model.ChequeDate,
            model.Notes,
            model.StatusLabel);

    private static string BuildWhatsAppMessage(ReceiptShareModel model)
    {
        var amount = model.Amount.ToString("N2", CultureInfo.GetCultureInfo("en-PK"));
        var message =
            $"Dear {model.CustomerName},\n\n" +
            $"Receipt: {model.ReceiptNumber}\n" +
            $"Customer: {model.CustomerCode}\n" +
            $"Date: {model.ReceiptDate:dd/MM/yyyy}\n" +
            $"Amount: PKR {amount}\n" +
            $"Payment: {model.PaymentMethodLabel}\n";

        if (!string.IsNullOrWhiteSpace(model.ChequeNumber))
        {
            message += $"Cheque #: {model.ChequeNumber}";
            if (model.ChequeDate.HasValue)
            {
                message += $" · Date: {model.ChequeDate.Value:dd/MM/yyyy}";
            }

            message += '\n';
        }

        return message +
               "\nPlease find the receipt PDF attached or request it from us.\n\n" +
               $"Regards,\n{model.CompanyName}";
    }

    private static string GetPaymentMethodLabel(PaymentMethod paymentMethod, ChequeBankType? chequeBankType) =>
        paymentMethod switch
        {
            PaymentMethod.Cheque when chequeBankType == ChequeBankType.SameBank => "Cheque (Same Bank)",
            PaymentMethod.Cheque when chequeBankType == ChequeBankType.OtherBank => "Cheque (Other Bank)",
            PaymentMethod.Cheque => "Cheque",
            PaymentMethod.BankTransfer => "Bank Transfer",
            _ => "Cash"
        };

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
        string ReceiptNumber,
        string CustomerName,
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
