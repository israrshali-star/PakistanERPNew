using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Domain.Entities;

public class DatabaseBackupHistory : AuditableEntity
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public JobRunType RunType { get; set; } = JobRunType.Manual;
    public JobRunStatus Status { get; set; } = JobRunStatus.Running;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
