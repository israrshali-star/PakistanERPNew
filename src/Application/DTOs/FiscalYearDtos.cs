namespace PakistanAccountingERP.Application.DTOs;

public record FiscalYearDto(
    int Id,
    string Code,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    bool IsActive,
    bool IsClosed);

public record FiscalYearListItemDto(
    int Id,
    string Code,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    bool IsActive,
    bool IsClosed);

public class FiscalYearSaveRequest
{
    public int? Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }
    public bool IsClosed { get; set; }
}

public record FiscalYearSaveResult(bool Success, string? Message, FiscalYearDto? FiscalYear);

public record FiscalYearActionResult(bool Success, string? Message, FiscalYearDto? FiscalYear);
