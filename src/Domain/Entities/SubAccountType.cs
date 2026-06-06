namespace PakistanAccountingERP.Domain.Entities;

public class SubAccountType
{
    public int SubTypeId { get; set; }
    public int TypeId { get; set; }
    public string SubTypeName { get; set; } = string.Empty;
    public string SubTypeCode { get; set; } = string.Empty;

    public AccountType AccountType { get; set; } = null!;
    public ICollection<ChartOfAccount> ChartOfAccounts { get; set; } = new List<ChartOfAccount>();
}
