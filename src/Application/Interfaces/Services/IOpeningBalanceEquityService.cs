namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Keeps Opening Balance Equity (30000) in sync so the trial balance stays balanced
/// after opening-balance changes on COA, customers, or vendors.
/// </summary>
public interface IOpeningBalanceEquityService
{
    /// <summary>
    /// Recalculates and updates OBE opening for the company. Does not start a transaction;
    /// call within the caller's unit of work / SaveChanges.
    /// </summary>
    Task EnsureBalancedAsync(int companyId, CancellationToken cancellationToken = default);
}
