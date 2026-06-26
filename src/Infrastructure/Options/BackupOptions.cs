namespace PakistanAccountingERP.Infrastructure.Options;

public class BackupOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalHours { get; set; } = 24;
    public int RetentionDays { get; set; } = 14;
    public string StoragePath { get; set; } = string.Empty;
}
