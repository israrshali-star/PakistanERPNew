using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ILedgerPdfService
{
    byte[] GeneratePdf(PartyLedgerPdfDto model);
}
