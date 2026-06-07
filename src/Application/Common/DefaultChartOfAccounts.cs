using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Common;

public static class DefaultChartOfAccounts
{
    public static IReadOnlyList<ChartOfAccount> CreateForCompany(int companyId, string createdBy, DateTime createdAt) =>
    [
        Create(companyId, "1100", "Cash In Hand", 1, 1, createdBy, createdAt),
        Create(companyId, "1200", "Accounts Receivable", 1, 2, createdBy, createdAt),
        Create(companyId, "1300", "Inventory", 1, 3, createdBy, createdAt),
        Create(companyId, "1400", "Input Tax Recoverable", 1, 6, createdBy, createdAt),
        Create(companyId, "1500", "Fixed Assets", 1, 5, createdBy, createdAt),
        Create(companyId, "2100", "Accounts Payable", 2, 8, createdBy, createdAt),
        Create(companyId, "2200", "Sales Tax Payable", 2, 10, createdBy, createdAt),
        Create(companyId, "2300", "Accrued Liabilities", 2, 9, createdBy, createdAt),
        Create(companyId, "3100", "Owner's Capital", 3, 14, createdBy, createdAt),
        Create(companyId, "3200", "Retained Earnings", 3, 15, createdBy, createdAt),
        Create(companyId, "4100", "Sales Revenue", 4, 18, createdBy, createdAt),
        Create(companyId, "4200", "Sales Returns", 4, 19, createdBy, createdAt),
        Create(companyId, "5100", "Purchases", 5, 22, createdBy, createdAt),
        Create(companyId, "5200", "Freight In", 5, 25, createdBy, createdAt),
        Create(companyId, "6100", "Administrative Expenses", 6, 28, createdBy, createdAt),
        Create(companyId, "6200", "Selling & Marketing", 6, 29, createdBy, createdAt),
        Create(companyId, "6300", "Payroll & Benefits", 6, 30, createdBy, createdAt)
    ];

    private static ChartOfAccount Create(
        int companyId,
        string number,
        string name,
        int typeId,
        int subTypeId,
        string createdBy,
        DateTime createdAt) =>
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
            CreatedBy = createdBy
        };
}
