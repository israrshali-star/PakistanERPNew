namespace PakistanAccountingERP.Application.DTOs;

public record AuditLogListItemDto(
    long Id,
    DateTime CreatedAt,
    string Action,
    string? TableName,
    string? RecordId,
    string? UserName,
    string? IpAddress,
    string? CompanyName);

public record AuditLogDetailDto(
    long Id,
    DateTime CreatedAt,
    string Action,
    string? TableName,
    string? RecordId,
    string? UserId,
    string? UserName,
    string? IpAddress,
    int? CompanyId,
    string? CompanyName,
    string? OldValue,
    string? NewValue);
