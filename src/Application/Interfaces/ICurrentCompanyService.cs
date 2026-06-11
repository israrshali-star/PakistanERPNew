namespace PakistanAccountingERP.Application.Interfaces;

/// <summary>
/// Provides the active company context for multi-tenant queries.
/// Implementation will read from session after authentication (Step 5).
/// </summary>
public interface ICurrentCompanyService
{
    /// <summary>Currently selected company id from session, or null if not set.</summary>
    int? CompanyId { get; }

    /// <summary>True when company was selected at login and cannot be changed until sign-out.</summary>
    bool IsCompanyLocked { get; }

    /// <summary>Returns <see cref="CompanyId"/> or throws if no company is selected.</summary>
    int GetRequiredCompanyId();

    /// <summary>Persists the selected company to session.</summary>
    Task SetCompanyAsync(int companyId, CancellationToken cancellationToken = default);

    /// <summary>Locks the session to the current company until sign-out.</summary>
    Task LockCompanyAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the selected company and session lock from session.</summary>
    Task ClearCompanyAsync(CancellationToken cancellationToken = default);
}
