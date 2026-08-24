namespace PakistanAccountingERP.Application.Options;

public class AttachmentOptions
{
    public string StoragePath { get; set; } = @"C:\ProgramData\PakistanAccountingERP\Attachments";
    public int MaxFileSizeMb { get; set; } = 10;
    public int MaxFilesPerInvoice { get; set; } = 10;
    public int MaxFilesPerBill { get; set; } = 10;
    public int MaxFilesPerReceipt { get; set; } = 10;
}
