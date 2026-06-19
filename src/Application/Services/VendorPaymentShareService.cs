using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class VendorPaymentShareService : IVendorPaymentShareService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly IVendorPaymentPdfService _paymentPdfService;

    public VendorPaymentShareService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        IVendorPaymentPdfService paymentPdfService)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _paymentPdfService = paymentPdfService;
    }

    public async Task<VendorPaymentShareInfoDto?> GetShareInfoAsync(
        int paymentId,
        CancellationToken cancellationToken = default)
    {
        var model = await LoadPaymentShareModelAsync(paymentId, cancellationToken);
        if (model is null)
        {
            return null;
        }

        return new VendorPaymentShareInfoDto(
            model.PaymentId,
            model.PaymentNumber,
            model.VendorName,
            model.VendorCode,
            model.PaymentDate,
            model.Amount,
            model.PaymentMethodLabel,
            model.VendorEmail,
            model.VendorPhone,
            model.CompanyName,
            BuildWhatsAppMessage(model));
    }

    public async Task<byte[]?> GetPaymentPdfAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        var model = await LoadPaymentShareModelAsync(paymentId, cancellationToken);
        if (model is null)
        {
            return null;
        }

        return _paymentPdfService.GeneratePdf(MapPdfDto(model));
    }

    private async Task<PaymentShareModel?> LoadPaymentShareModelAsync(
        int paymentId,
        CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.CompanyId;
        if (!companyId.HasValue)
        {
            return null;
        }

        var row = await _unitOfWork.Repository<VendorPayment>()
            .Query()
            .Where(p => p.Id == paymentId && p.CompanyId == companyId.Value)
            .Select(p => new
            {
                p.Id,
                p.PaymentNumber,
                p.PaymentDate,
                p.Amount,
                p.PaymentMethod,
                p.ChequeNumber,
                p.ChequeDate,
                p.Notes,
                VendorName = p.Vendor.VendorName,
                VendorCode = p.Vendor.VendorCode,
                VendorEmail = p.Vendor.Email,
                VendorPhone = p.Vendor.Phone,
                BankName = p.Bank != null ? p.Bank.BankName : null,
                CompanyName = p.Company.CompanyName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new PaymentShareModel(
            row.Id,
            row.PaymentNumber,
            row.VendorName,
            row.VendorCode,
            row.PaymentDate,
            row.Amount,
            GetPaymentMethodLabel(row.PaymentMethod),
            row.BankName,
            row.ChequeNumber,
            row.ChequeDate,
            row.Notes,
            row.VendorEmail,
            row.VendorPhone,
            row.CompanyName);
    }

    private static VendorPaymentPdfDto MapPdfDto(PaymentShareModel model) =>
        new(
            model.CompanyName,
            model.PaymentNumber,
            model.VendorName,
            model.VendorCode,
            model.PaymentDate,
            model.Amount,
            model.PaymentMethodLabel,
            model.BankName,
            model.ChequeNumber,
            model.ChequeDate,
            model.Notes);

    private static string BuildWhatsAppMessage(PaymentShareModel model)
    {
        var amount = model.Amount.ToString("N2", CultureInfo.GetCultureInfo("en-PK"));
        var amountWords = AmountInWords.ToPakistaniRupees(model.Amount);
        var message =
            $"Dear {model.VendorName},\n\n" +
            $"Payment: {model.PaymentNumber}\n" +
            $"Vendor: {model.VendorCode}\n" +
            $"Date: {model.PaymentDate:dd/MM/yyyy}\n" +
            $"Amount: PKR {amount}\n" +
            $"Amount in words: {amountWords}\n" +
            $"Payment mode: {model.PaymentMethodLabel}\n";

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
               "\nPlease find the payment voucher PDF attached or request it from us.\n\n" +
               $"Regards,\n{model.CompanyName}";
    }

    private static string GetPaymentMethodLabel(PaymentMethod paymentMethod) =>
        paymentMethod switch
        {
            PaymentMethod.Cheque => "Cheque",
            PaymentMethod.BankTransfer => "Bank Transfer",
            _ => "Cash"
        };

    private sealed record PaymentShareModel(
        int PaymentId,
        string PaymentNumber,
        string VendorName,
        string VendorCode,
        DateTime PaymentDate,
        decimal Amount,
        string PaymentMethodLabel,
        string? BankName,
        string? ChequeNumber,
        DateTime? ChequeDate,
        string? Notes,
        string? VendorEmail,
        string? VendorPhone,
        string CompanyName);
}
