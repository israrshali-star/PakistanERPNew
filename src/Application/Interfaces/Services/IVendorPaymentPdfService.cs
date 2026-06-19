using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IVendorPaymentPdfService
{
    byte[] GeneratePdf(VendorPaymentPdfDto model);
}
