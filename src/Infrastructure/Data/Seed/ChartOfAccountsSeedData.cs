using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Seed;

public static class ChartOfAccountsSeedData
{
    public static IReadOnlyList<ChartOfAccount> GetDefaultAccounts(int companyId, DateTime createdAt)
    {
        return
        [
            Create(companyId, "1100", "Cash In Hand", 1, 1, createdAt),
            Create(companyId, "1200", "Accounts Receivable", 1, 2, createdAt),
            Create(companyId, "1300", "Inventory", 1, 3, createdAt),
            Create(companyId, "1400", "Input Tax Recoverable", 1, 6, createdAt),
            Create(companyId, "1500", "Fixed Assets", 1, 5, createdAt),
            Create(companyId, "2100", "Accounts Payable", 2, 8, createdAt),
            Create(companyId, "2200", "Sales Tax Payable", 2, 10, createdAt),
            Create(companyId, "2300", "Accrued Liabilities", 2, 9, createdAt),
            Create(companyId, "3100", "Owner's Capital", 3, 14, createdAt),
            Create(companyId, "3200", "Retained Earnings", 3, 15, createdAt),
            Create(companyId, "4100", "Sales Revenue", 4, 18, createdAt),
            Create(companyId, "4200", "Sales Returns", 4, 19, createdAt),
            Create(companyId, "5100", "Purchases", 5, 22, createdAt),
            Create(companyId, "5200", "Freight In", 5, 25, createdAt),
            Create(companyId, "6100", "Administrative Expenses", 6, 28, createdAt),
            Create(companyId, "6200", "Selling & Marketing", 6, 29, createdAt),
            Create(companyId, "6300", "Payroll & Benefits", 6, 30, createdAt)
        ];
    }

    private static ChartOfAccount Create(int companyId, string number, string name, int typeId, int subTypeId, DateTime createdAt) =>
        new()
        {
            CompanyId = companyId,
            AccountNumber = number,
            AccountName = name,
            TypeId = typeId,
            SubTypeId = subTypeId,
            IsActive = true,
            OpeningBalance = 0m,
            CreatedAt = createdAt,
            CreatedBy = "system"
        };
}
