using PakistanAccountingERP.Domain.Common;

namespace PakistanAccountingERP.Application.Interfaces;

/// <summary>
/// Query helpers for entities scoped to the current company.
/// </summary>
public interface ICompanyScopedRepository<TEntity> where TEntity : CompanyAuditableEntity
{
    /// <summary>Queryable filtered by company id.</summary>
    IQueryable<TEntity> QueryForCompany(int companyId, bool asNoTracking = true);
}
