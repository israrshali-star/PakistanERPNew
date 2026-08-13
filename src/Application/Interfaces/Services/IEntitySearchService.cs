using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IEntitySearchService
{
    Task<EntitySearchResponse> SearchAsync(
        string entity,
        string? query,
        string? id,
        int limit,
        string? itemType,
        CancellationToken cancellationToken = default);
}
