namespace PakistanAccountingERP.Application.DTOs;

public sealed class OpeningStockRepairResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int BillLinesUpdated { get; init; }
    public int TransactionsUpdated { get; init; }
    public int ItemsRecalculated { get; init; }
}
