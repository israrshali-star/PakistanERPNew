using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface ICustomerBalancePdfService
{
    byte[] GeneratePdf(CustomerBalanceReportDto report, string companyName);
}
