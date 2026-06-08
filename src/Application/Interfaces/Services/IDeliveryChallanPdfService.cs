using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IDeliveryChallanPdfService
{
    byte[] GeneratePdf(DeliveryChallanPrintDto model);
}
