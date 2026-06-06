using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Company-scoped vendor management, ledger, and statements.
/// </summary>
public interface IVendorService
{
    Task<DataTableResponse<VendorListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    Task<VendorDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<NextVendorCodeDto> GenerateNextVendorCodeAsync(CancellationToken cancellationToken = default);

    Task<VendorSaveResult> CreateAsync(
        VendorSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<VendorSaveResult> UpdateAsync(
        VendorSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<VendorSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<VendorLedgerDto?> GetLedgerAsync(int id, CancellationToken cancellationToken = default);

    Task<VendorStatementDto?> GetStatementAsync(
        int id,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);
}
