namespace PakistanAccountingERP.Application.DTOs;

public record EntitySearchResponse(IReadOnlyList<EntitySearchItemDto> Results);

public class EntitySearchItemDto
{
    public string Id { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string? Group { get; init; }
    public string? BuyerId { get; init; }
    public string? BuyerName { get; init; }
    public string? VendorCode { get; init; }
    public string? VendorName { get; init; }
    public string? ItemCode { get; init; }
    public string? ItemName { get; init; }
    public string? AccountNumber { get; init; }
    public string? AccountName { get; init; }
    public string? BankName { get; init; }
    public string? AccountTitle { get; init; }
    public string? Code { get; init; }
    public string? Name { get; init; }
    public string? Address { get; init; }
    public string? Ntn { get; init; }
    public string? Cnic { get; init; }
    public string? LotNo { get; init; }
    public string? StackNo { get; init; }
    public string? UnitSymbol { get; init; }
    public string? PartyType { get; init; }
    public string? PartyName { get; init; }
    public string? PartyCode { get; init; }
    public decimal? Balance { get; init; }
    public decimal? CurrentStock { get; init; }
    public decimal? FurtherTaxRate { get; init; }
    public decimal? DefaultTaxRate { get; init; }
    public int? ScenarioId { get; init; }
    public int? ProvinceId { get; init; }
    public int? InvoiceType { get; init; }
    public int? CustomerId { get; init; }
    public int? VendorId { get; init; }
    public int? ChartOfAccountId { get; init; }
}
