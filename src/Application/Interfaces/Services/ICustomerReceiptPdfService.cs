using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ICustomerReceiptPdfService
{
    byte[] GeneratePdf(CustomerReceiptPdfDto model);
}
