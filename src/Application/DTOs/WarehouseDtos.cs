namespace PakistanAccountingERP.Application.DTOs;

public record WarehouseDto(
    int Id,
    string Code,
    string Name,
    string? Address,
    bool IsActive,
    int TransactionCount);

public record WarehouseListItemDto(
    int Id,
    string Code,
    string Name,
    string? Address,
    bool IsActive,
    int TransactionCount);

public class WarehouseSaveRequest
{
    public int? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

public record WarehouseSaveResult(bool Success, string? Message, WarehouseDto? Warehouse);

public record NextWarehouseCodeDto(string Code);
