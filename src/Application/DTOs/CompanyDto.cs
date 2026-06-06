namespace PakistanAccountingERP.Application.DTOs;

public record CompanyDto(
    int Id,
    string CompanyName,
    string? NTN,
    bool IsDefault);
