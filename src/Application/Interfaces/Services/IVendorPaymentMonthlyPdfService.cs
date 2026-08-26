using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IVendorPaymentMonthlyPdfService
{
    byte[] GeneratePdf(VendorPaymentMonthlyReportDto report, string companyName);
}
