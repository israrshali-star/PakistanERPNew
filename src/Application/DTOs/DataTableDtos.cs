namespace PakistanAccountingERP.Application.DTOs;

public record DataTableRequest(
    int Draw,
    int Start,
    int Length,
    string? SearchValue,
    int OrderColumn,
    string OrderDirection);

public record DataTableResponse<T>(
    int Draw,
    int RecordsTotal,
    int RecordsFiltered,
    IReadOnlyList<T> Data);
