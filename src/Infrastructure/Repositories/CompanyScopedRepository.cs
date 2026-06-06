using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Infrastructure.Data;

namespace PakistanAccountingERP.Infrastructure.Repositories;

public class CompanyScopedRepository<TEntity> : Repository<TEntity>, ICompanyScopedRepository<TEntity>
    where TEntity : CompanyAuditableEntity
{
    public CompanyScopedRepository(AppDbContext context) : base(context)
    {
    }

    public IQueryable<TEntity> QueryForCompany(int companyId, bool asNoTracking = true) =>
        Query(asNoTracking).Where(x => x.CompanyId == companyId);
}
