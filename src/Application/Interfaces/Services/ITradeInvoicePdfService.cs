using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ITradeInvoicePdfService
{
    byte[] GeneratePdf(TradeInvoicePrintDto model);
}
