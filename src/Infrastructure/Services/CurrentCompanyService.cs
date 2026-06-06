using Microsoft.AspNetCore.Http;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.Interfaces;

namespace PakistanAccountingERP.Infrastructure.Services;

public class CurrentCompanyService : ICurrentCompanyService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentCompanyService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ISession? Session => _httpContextAccessor.HttpContext?.Session;

    public int? CompanyId
    {
        get
        {
            var value = Session?.GetInt32(SessionKeys.CompanyId);
            return value;
        }
    }

    public int GetRequiredCompanyId() =>
        CompanyId ?? throw new InvalidOperationException("No company is selected. Please select a company.");

    public Task SetCompanyAsync(int companyId, CancellationToken cancellationToken = default)
    {
        Session?.SetInt32(SessionKeys.CompanyId, companyId);
        return Task.CompletedTask;
    }

    public Task ClearCompanyAsync(CancellationToken cancellationToken = default)
    {
        Session?.Remove(SessionKeys.CompanyId);
        return Task.CompletedTask;
    }
}
