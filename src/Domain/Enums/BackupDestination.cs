namespace PakistanAccountingERP.Domain.Enums;

public enum BackupDestination
{
    /// <summary>Store on the ERP server (listed in backup history).</summary>
    Online = 1,

    /// <summary>Download to the user's computer (browser save dialog).</summary>
    Local = 2
}
