using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Company-scoped chart of accounts management.
/// </summary>
public interface IChartOfAccountsService
{
    /// <summary>Returns accounts grouped by account type and sub-type for the tree view.</summary>
    Task<IReadOnlyList<ChartOfAccountTreeTypeDto>> GetTreeAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Returns a single account with running balance metadata.</summary>
    Task<ChartOfAccountDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Finds an account by number for the active company.</summary>
    Task<ChartOfAccountDto?> GetByAccountNumberAsync(
        string accountNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Returns header accounts that can be selected as a parent.</summary>
    Task<IReadOnlyList<ParentAccountLookupDto>> GetParentAccountsAsync(
        int typeId,
        int subTypeId,
        int? excludeAccountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Suggests the next account number for a type/sub-type (or under a parent).</summary>
    Task<SuggestedAccountNumberDto> SuggestAccountNumberAsync(
        int typeId,
        int subTypeId,
        int? parentAccountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new ledger account for the active company.</summary>
    Task<ChartOfAccountSaveResult> CreateAsync(
        ChartOfAccountSaveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates an existing ledger account.</summary>
    Task<ChartOfAccountSaveResult> UpdateAsync(
        ChartOfAccountSaveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes an account when it has no journal lines or bank links.</summary>
    Task<ChartOfAccountSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Exports the chart of accounts to an Excel workbook.</summary>
    Task<byte[]> ExportToExcelAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns posted GL ledger entries with running balance for a leaf account.</summary>
    Task<ChartOfAccountLedgerDto?> GetLedgerAsync(
        int id,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>Exports account ledger entries to an Excel workbook.</summary>
    Task<byte[]?> ExportLedgerToExcelAsync(
        int id,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>Exports account ledger entries to a PDF document.</summary>
    Task<byte[]?> ExportLedgerToPdfAsync(
        int id,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);
}
