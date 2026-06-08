namespace PakistanAccountingERP.Application.DTOs;

public sealed record CustomReportColumnDto(
    string Key,
    string Label,
    string DataType);

public sealed record CustomReportSourceDto(
    string Key,
    string Name,
    string Description,
    bool SupportsDateFilter,
    string? DateColumnLabel,
    IReadOnlyList<CustomReportColumnDto> Columns);

public sealed class CustomReportRunRequest
{
    public string SourceKey { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = [];
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int MaxRows { get; set; } = 5000;
}

public sealed record CustomReportRunResult(
    string SourceName,
    IReadOnlyList<CustomReportColumnDto> Columns,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    int TotalRows,
    bool Truncated);
