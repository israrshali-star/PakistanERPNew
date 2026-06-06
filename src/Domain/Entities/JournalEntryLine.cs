namespace PakistanAccountingERP.Domain.Entities;

public class JournalEntryLine
{
    public int Id { get; set; }
    public int JournalEntryId { get; set; }
    public int ChartOfAccountId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Memo { get; set; }

    public JournalEntry JournalEntry { get; set; } = null!;
    public ChartOfAccount ChartOfAccount { get; set; } = null!;
}
