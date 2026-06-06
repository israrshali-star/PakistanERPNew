using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Services;

public class AuditService : IAuditService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentCompanyService _currentCompany;

    public AuditService(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        ICurrentCompanyService currentCompany)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public async Task LogAsync(
        string action,
        string tableName,
        string recordId,
        string? oldValue = null,
        string? newValue = null,
        CancellationToken cancellationToken = default)
    {
        await WriteAsync(action, tableName, recordId, oldValue, newValue, null, cancellationToken);
    }

    public Task LogLoginAsync(
        string userId,
        string userName,
        string ipAddress,
        CancellationToken cancellationToken = default) =>
        WriteAsync("Login", "AspNetUsers", userId, null, null, ipAddress, cancellationToken, userId, userName);

    public Task LogErrorAsync(
        string action,
        string errorMessage,
        CancellationToken cancellationToken = default) =>
        WriteAsync(action, "Error", string.Empty, null, errorMessage, null, cancellationToken);

    private async Task WriteAsync(
        string action,
        string tableName,
        string recordId,
        string? oldValue,
        string? newValue,
        string? ipAddress,
        CancellationToken cancellationToken,
        string? userId = null,
        string? userName = null)
    {
        var entry = new AuditLog
        {
            Action = action,
            TableName = tableName,
            RecordId = recordId,
            OldValue = oldValue,
            NewValue = newValue,
            IPAddress = ipAddress ?? _currentUser.IpAddress,
            UserId = userId ?? _currentUser.UserId,
            UserName = userName ?? _currentUser.UserName,
            CompanyId = _currentCompany.CompanyId,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Repository<AuditLog>().AddAsync(entry, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
