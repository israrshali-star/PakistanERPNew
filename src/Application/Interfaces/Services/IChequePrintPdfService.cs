using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IChequePrintPdfService
{
    byte[] GeneratePdf(ChequePrintDto model);
}
