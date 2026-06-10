using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Seed;

public static class ChartOfAccountsSeedData
{
    public static IReadOnlyList<ChartOfAccount> GetDefaultAccounts(int companyId, DateTime createdAt)
    {
        return
        [
            Create(companyId, GlAccountNumbers.CashInHand, "Cash In Hand", 1, 1, createdAt),
            Create(companyId, GlAccountNumbers.UndepositedFunds, "Undeposited Funds", 1, 1, createdAt),
            Create(companyId, GlAccountNumbers.AccountsReceivable, "Accounts Receivable", 1, 2, createdAt),
            Create(companyId, GlAccountNumbers.InventoryAsset, "Inventory Asset", 1, 3, createdAt),
            Create(companyId, GlAccountNumbers.PrepaidSalesTax, "Pre Paid Sales Tax", 1, 6, createdAt),
            Create(companyId, GlAccountNumbers.FixedAssets, "Fixed Assets", 1, 5, createdAt),
            Create(companyId, GlAccountNumbers.AccountsPayable, "Account Payable", 2, 8, createdAt),
            Create(companyId, GlAccountNumbers.SalesTaxPayable, "Sales Tax Payable", 2, 10, createdAt),
            Create(companyId, GlAccountNumbers.AccruedLiabilities, "Accrued Liabilities", 2, 9, createdAt),
            Create(companyId, GlAccountNumbers.CartagePayable, "Cartage Payable", 2, 9, createdAt),
            Create(companyId, GlAccountNumbers.OwnersCapital, "Owner's Capital", 3, 14, createdAt),
            Create(companyId, GlAccountNumbers.RetainedEarnings, "Retained Earnings", 3, 15, createdAt),
            Create(companyId, GlAccountNumbers.SalesRevenue, "Sales Revenue", 4, 18, createdAt),
            Create(companyId, GlAccountNumbers.SalesReturns, "Sales Returns", 4, 19, createdAt),
            Create(companyId, GlAccountNumbers.Purchases, "Purchases", 5, 22, createdAt),
            Create(companyId, GlAccountNumbers.FreightIn, "Freight In", 5, 25, createdAt),
            Create(companyId, GlAccountNumbers.AdministrativeExpenses, "Administrative Expenses", 6, 28, createdAt),
            Create(companyId, GlAccountNumbers.SellingAndMarketing, "Selling & Marketing", 6, 29, createdAt),
            Create(companyId, GlAccountNumbers.PayrollAndBenefits, "Payroll & Benefits", 6, 30, createdAt)
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
