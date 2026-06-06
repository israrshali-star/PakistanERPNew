namespace PakistanAccountingERP.Application.Interfaces;

/// <summary>
/// Provides the active company context for multi-tenant queries.
/// Implementation will read from session after authentication (Step 5).
/// </summary>
public interface ICurrentCompanyService
{
    /// <summary>Currently selected company id from session, or null if not set.</summary>
    int? CompanyId { get; }

    /// <summary>Returns <see cref="CompanyId"/> or throws if no company is selected.</summary>
    int GetRequiredCompanyId();

    /// <summary>Persists the selected company to session.</summary>
    Task SetCompanyAsync(int companyId, CancellationToken cancellationToken = default);

    /// <summary>Removes the selected company from session.</summary>
    Task ClearCompanyAsync(CancellationToken cancellationToken = default);
}
