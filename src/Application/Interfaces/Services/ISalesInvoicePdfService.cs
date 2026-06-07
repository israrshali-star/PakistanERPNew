using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ISalesInvoicePdfService
{
    byte[] GeneratePdf(SalesInvoicePrintDto model);
}
