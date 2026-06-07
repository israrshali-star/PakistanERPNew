namespace PakistanAccountingERP.Application.DTOs;

public record CompanyListItemDto(
    int Id,
    string CompanyName,
    string? NTN,
    string? ProvinceName,
    string? Phone,
    string? Email,
    bool IsDefault);

public record CompanyDetailDto(
    int Id,
    string CompanyName,
    string? Address,
    string? NTN,
    int? ProvinceId,
    string? ProvinceName,
    string? Phone,
    string? Email,
    bool IsDefault);

public record CompanySaveRequest(
    int? Id,
    string CompanyName,
    string? Address,
    string? NTN,
    int? ProvinceId,
    string? Phone,
    string? Email,
    bool IsDefault);

public record CompanySaveResult(bool Success, string? Message, CompanyDetailDto? Company);
