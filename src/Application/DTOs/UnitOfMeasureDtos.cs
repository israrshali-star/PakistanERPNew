namespace PakistanAccountingERP.Application.DTOs;

public record UnitOfMeasureDto(
    int Id,
    string Name,
    string? Symbol,
    int ItemCount);

public record UnitOfMeasureListItemDto(
    int Id,
    string Name,
    string? Symbol,
    int ItemCount);

public class UnitOfMeasureSaveRequest
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Symbol { get; set; }
}

public record UnitOfMeasureSaveResult(bool Success, string? Message, UnitOfMeasureDto? Unit);
