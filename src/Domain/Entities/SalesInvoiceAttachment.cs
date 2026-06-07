namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;

public class SalesInvoiceAttachment : CompanyAuditableEntity
{
    public int Id { get; set; }
    public int SalesInvoiceId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string RelativePath { get; set; } = string.Empty;

    public SalesInvoice SalesInvoice { get; set; } = null!;
    public Company Company { get; set; } = null!;
}
