namespace PakistanAccountingERP.Application.DTOs;

public record ItemCategoryDto(
    int Id,
    string Name,
    string? Description,
    int ItemCount);

public record ItemCategoryListItemDto(
    int Id,
    string Name,
    string? Description,
    int ItemCount);

public class ItemCategorySaveRequest
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public record ItemCategorySaveResult(bool Success, string? Message, ItemCategoryDto? Category);
