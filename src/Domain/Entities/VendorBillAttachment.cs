namespace PakistanAccountingERP.Domain.Entities;

using PakistanAccountingERP.Domain.Common;

public class VendorBillAttachment : CompanyAuditableEntity
{
    public int Id { get; set; }
    public int VendorBillId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string RelativePath { get; set; } = string.Empty;

    public VendorBill VendorBill { get; set; } = null!;
    public Company Company { get; set; } = null!;
}
