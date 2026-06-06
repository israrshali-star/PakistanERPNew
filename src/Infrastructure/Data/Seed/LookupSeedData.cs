using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Infrastructure.Data.Seed;

public static class LookupSeedData
{
    public static IReadOnlyList<Province> GetProvinces() =>
    [
        new() { Id = 1, Name = "PUNJAB", Code = "PB" },
        new() { Id = 2, Name = "SINDH", Code = "SD" },
        new() { Id = 3, Name = "KHYBER PAKHTUNKHWA", Code = "KP" },
        new() { Id = 4, Name = "BALOCHISTAN", Code = "BC" },
        new() { Id = 5, Name = "CAPITAL TERRITORY", Code = "ICT" },
        new() { Id = 6, Name = "AZAD JAMMU AND KASHMIR", Code = "AJK" },
        new() { Id = 7, Name = "GILGIT BALTISTAN", Code = "GB" },
        new() { Id = 8, Name = "FATA/PATA", Code = "FATA" }
    ];

    public static IReadOnlyList<UnitOfMeasure> GetUnitsOfMeasure() =>
    [
        new() { Id = 1, Name = "Kilogram", Symbol = "KG" },
        new() { Id = 2, Name = "Pound", Symbol = "LB" },
        new() { Id = 3, Name = "Per Piece", Symbol = "PCS" },
        new() { Id = 4, Name = "Carton", Symbol = "CTN" },
        new() { Id = 5, Name = "Litre", Symbol = "LTR" },
        new() { Id = 6, Name = "Meter", Symbol = "MTR" }
    ];

    public static IReadOnlyList<AccountType> GetAccountTypes() =>
    [
        new() { TypeId = 1, TypeCode = "ASSET", TypeName = "Assets", CreatedAt = DateTime.UtcNow },
        new() { TypeId = 2, TypeCode = "LIABILITY", TypeName = "Liabilities", CreatedAt = DateTime.UtcNow },
        new() { TypeId = 3, TypeCode = "EQUITY", TypeName = "Equity", CreatedAt = DateTime.UtcNow },
        new() { TypeId = 4, TypeCode = "REVENUE", TypeName = "Revenue", CreatedAt = DateTime.UtcNow },
        new() { TypeId = 5, TypeCode = "COGS", TypeName = "Cost of Goods Sold", CreatedAt = DateTime.UtcNow },
        new() { TypeId = 6, TypeCode = "EXPENSE", TypeName = "Expenses", CreatedAt = DateTime.UtcNow }
    ];

    public static IReadOnlyList<SubAccountType> GetSubAccountTypes() =>
    [
        new() { SubTypeId = 1, TypeId = 1, SubTypeCode = "CASH", SubTypeName = "Cash & Bank" },
        new() { SubTypeId = 2, TypeId = 1, SubTypeCode = "AR", SubTypeName = "Accounts Receivable" },
        new() { SubTypeId = 3, TypeId = 1, SubTypeCode = "INVENTORY", SubTypeName = "Inventory" },
        new() { SubTypeId = 4, TypeId = 1, SubTypeCode = "PREPAID", SubTypeName = "Prepaid Expenses" },
        new() { SubTypeId = 5, TypeId = 1, SubTypeCode = "FIXED", SubTypeName = "Fixed Assets" },
        new() { SubTypeId = 6, TypeId = 1, SubTypeCode = "INPUT_TAX", SubTypeName = "Input Tax Recoverable" },
        new() { SubTypeId = 7, TypeId = 1, SubTypeCode = "OTHER_ASSET", SubTypeName = "Other Assets" },
        new() { SubTypeId = 8, TypeId = 2, SubTypeCode = "AP", SubTypeName = "Accounts Payable" },
        new() { SubTypeId = 9, TypeId = 2, SubTypeCode = "ACCRUED", SubTypeName = "Accrued Liabilities" },
        new() { SubTypeId = 10, TypeId = 2, SubTypeCode = "TAX_PAYABLE", SubTypeName = "Tax Payable" },
        new() { SubTypeId = 11, TypeId = 2, SubTypeCode = "LOAN_ST", SubTypeName = "Short-term Loans" },
        new() { SubTypeId = 12, TypeId = 2, SubTypeCode = "LOAN_LT", SubTypeName = "Long-term Loans" },
        new() { SubTypeId = 13, TypeId = 2, SubTypeCode = "OTHER_LIAB", SubTypeName = "Other Liabilities" },
        new() { SubTypeId = 14, TypeId = 3, SubTypeCode = "CAPITAL", SubTypeName = "Owner's Capital" },
        new() { SubTypeId = 15, TypeId = 3, SubTypeCode = "RETAINED", SubTypeName = "Retained Earnings" },
        new() { SubTypeId = 16, TypeId = 3, SubTypeCode = "DRAWINGS", SubTypeName = "Owner's Drawings" },
        new() { SubTypeId = 17, TypeId = 3, SubTypeCode = "RESERVES", SubTypeName = "Reserves" },
        new() { SubTypeId = 18, TypeId = 4, SubTypeCode = "SALES", SubTypeName = "Sales Revenue" },
        new() { SubTypeId = 19, TypeId = 4, SubTypeCode = "SALES_RETURN", SubTypeName = "Sales Returns" },
        new() { SubTypeId = 20, TypeId = 4, SubTypeCode = "OTHER_INCOME", SubTypeName = "Other Income" },
        new() { SubTypeId = 21, TypeId = 4, SubTypeCode = "DISCOUNT_GIVEN", SubTypeName = "Discount Allowed" },
        new() { SubTypeId = 22, TypeId = 5, SubTypeCode = "PURCHASE", SubTypeName = "Purchases" },
        new() { SubTypeId = 23, TypeId = 5, SubTypeCode = "DIRECT_LABOR", SubTypeName = "Direct Labor" },
        new() { SubTypeId = 24, TypeId = 5, SubTypeCode = "DIRECT_OH", SubTypeName = "Direct Overhead" },
        new() { SubTypeId = 25, TypeId = 5, SubTypeCode = "FREIGHT_IN", SubTypeName = "Freight In" },
        new() { SubTypeId = 26, TypeId = 5, SubTypeCode = "PURCHASE_RETURN", SubTypeName = "Purchase Returns" },
        new() { SubTypeId = 27, TypeId = 5, SubTypeCode = "INV_ADJ", SubTypeName = "Inventory Adjustments" },
        new() { SubTypeId = 28, TypeId = 6, SubTypeCode = "ADMIN", SubTypeName = "Administrative Expenses" },
        new() { SubTypeId = 29, TypeId = 6, SubTypeCode = "SELLING", SubTypeName = "Selling & Marketing" },
        new() { SubTypeId = 30, TypeId = 6, SubTypeCode = "PAYROLL", SubTypeName = "Payroll & Benefits" },
        new() { SubTypeId = 31, TypeId = 6, SubTypeCode = "RENT", SubTypeName = "Rent & Utilities" },
        new() { SubTypeId = 32, TypeId = 6, SubTypeCode = "DEPRECIATION", SubTypeName = "Depreciation" },
        new() { SubTypeId = 33, TypeId = 6, SubTypeCode = "FINANCE", SubTypeName = "Finance Costs" },
        new() { SubTypeId = 34, TypeId = 6, SubTypeCode = "TAX_EXP", SubTypeName = "Tax Expense" },
        new() { SubTypeId = 35, TypeId = 6, SubTypeCode = "OTHER_EXP", SubTypeName = "Other Expenses" }
    ];

    public static IReadOnlyList<ScenarioType> GetScenarioTypes() =>
    [
        new() { ScenarioId = 1, Code = "SN001", Description = "Goods at Standard Rate to Registered Buyers" },
        new() { ScenarioId = 2, Code = "SN002", Description = "Goods at Standard Rate to UnRegistered Buyers" },
        new() { ScenarioId = 3, Code = "SN008", Description = "Sales of 3rd Schedule Goods" },
        new() { ScenarioId = 4, Code = "SN0026", Description = "Sale to End Consumer by Retailers" },
        new() { ScenarioId = 5, Code = "SN0027", Description = "Sale to End Consumer by Retailers" },
        new() { ScenarioId = 6, Code = "SN0028", Description = "Sale to End Consumer by Retailers" }
    ];
}
