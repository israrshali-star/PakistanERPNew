using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IAuditLogService
{
    Task<DataTableResponse<AuditLogListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<AuditLogDetailDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
}
