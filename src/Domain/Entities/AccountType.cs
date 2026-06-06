namespace PakistanAccountingERP.Domain.Entities;

public class AccountType
{
    public int TypeId { get; set; }
    public string TypeCode { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public ICollection<SubAccountType> SubAccountTypes { get; set; } = new List<SubAccountType>();
    public ICollection<ChartOfAccount> ChartOfAccounts { get; set; } = new List<ChartOfAccount>();
}
