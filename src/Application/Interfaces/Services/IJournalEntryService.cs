using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IJournalEntryService
{
    Task<DataTableResponse<JournalEntryListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<JournalEntryDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken = default);

    Task<NextJournalEntryNumberDto> GenerateNextEntryNumberAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JournalEntryAccountLookupDto>> GetAccountLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<JournalEntrySaveResult> CreateAsync(
        JournalEntrySaveRequest request,
        CancellationToken cancellationToken = default);

    Task<JournalEntrySaveResult> UpdateAsync(
        int id,
        JournalEntrySaveRequest request,
        CancellationToken cancellationToken = default);

    Task<JournalEntryActionResult> PostAsync(int id, CancellationToken cancellationToken = default);

    Task<JournalEntryActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
