using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Company-scoped customer management, ledger, and statements.
/// </summary>
public interface ICustomerService
{
    /// <summary>Returns paginated customers for DataTables server-side processing.</summary>
    Task<DataTableResponse<CustomerListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a single customer with computed balance.</summary>
    Task<CustomerDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Generates the next buyer id (CUST-0001 format) for the active company.</summary>
    Task<NextBuyerIdDto> GenerateNextBuyerIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a new customer.</summary>
    Task<CustomerSaveResult> CreateAsync(
        CustomerSaveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates an existing customer.</summary>
    Task<CustomerSaveResult> UpdateAsync(
        CustomerSaveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a customer when it has no invoices.</summary>
    Task<CustomerSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns the full customer ledger with running balance.</summary>
    Task<CustomerLedgerDto?> GetLedgerAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns a date-filtered customer statement.</summary>
    Task<CustomerStatementDto?> GetStatementAsync(
        int id,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);
}
