using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Common;

public static class DefaultChartOfAccounts
{
    public static IReadOnlyList<ChartOfAccount> CreateForCompany(int companyId, string createdBy, DateTime createdAt) =>
    [
        Create(companyId, GlAccountNumbers.CashInHand, "Cash In Hand", 1, 1, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.UndepositedFunds, "Undeposited Funds", 1, 1, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.AccountsReceivable, "Accounts Receivable", 1, 2, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.InventoryAsset, "Inventory Asset", 1, 3, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.PrepaidSalesTax, "Pre Paid Sales Tax", 1, 6, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.FixedAssets, "Fixed Assets", 1, 5, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.AccountsPayable, "Account Payable", 2, 8, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.SalesTaxPayable, "Sales Tax Payable", 2, 10, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.FurtherTaxPayable, "Further Tax Payable", 2, 10, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.SalesTaxPayable18, "Sales Tax Payable (18%)", 2, 10, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.AccruedLiabilities, "Accrued Liabilities", 2, 9, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.CartagePayable, "Cartage Payable", 2, 9, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.OwnersCapital, "Owner's Capital", 3, 14, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.RetainedEarnings, "Retained Earnings", 3, 15, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.SalesRevenue, "Sales Revenue", 4, 18, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.SalesReturns, "Sales Returns", 4, 19, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.Purchases, "Purchases", 5, 22, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.FreightIn, "Freight In", 5, 25, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.AdministrativeExpenses, "Administrative Expenses", 6, 28, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.SellingAndMarketing, "Selling & Marketing", 6, 29, createdBy, createdAt),
        Create(companyId, GlAccountNumbers.PayrollAndBenefits, "Payroll & Benefits", 6, 30, createdBy, createdAt)
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
