using PakistanAccountingERP.Domain.Common;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Domain.Entities;

public class DataExportHistory : CompanyAuditableEntity
{
    public int Id { get; set; }
    public DataExportType ExportType { get; set; } = DataExportType.ChartOfAccounts;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public JobRunStatus Status { get; set; } = JobRunStatus.Running;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public Company Company { get; set; } = null!;
}
