namespace PakistanAccountingERP.Application.Interfaces.Services;

/// <summary>
/// Writes audit trail entries for user actions and errors.
/// </summary>
public interface IAuditService
{
    Task LogAsync(
        string action,
        string tableName,
        string recordId,
        string? oldValue = null,
        string? newValue = null,
        CancellationToken cancellationToken = default);

    Task LogLoginAsync(
        string userId,
        string userName,
        string ipAddress,
        CancellationToken cancellationToken = default);

    Task LogErrorAsync(
        string action,
        string errorMessage,
        CancellationToken cancellationToken = default);
}
