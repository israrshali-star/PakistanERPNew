using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Import;

internal static class QuickBooksImportAccountResolver
{
    public static Task<int?> FindAccountsReceivableAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        CancellationToken cancellationToken = default) =>
        FindAccountAsync(
            unitOfWork,
            companyId,
            ["11110", "11000", "1200"],
            subTypeId: 2,
            nameContains: "Accounts Receivable",
            cancellationToken);

    public static Task<int?> FindAccountsPayableAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        CancellationToken cancellationToken = default) =>
        FindAccountAsync(
            unitOfWork,
            companyId,
            ["20000", "2100"],
            subTypeId: 8,
            nameContains: "Accounts Payable",
            cancellationToken);

    public static Task<int?> FindSalesRevenueAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        CancellationToken cancellationToken = default) =>
        FindAccountAsync(
            unitOfWork,
            companyId,
            ["47910", "47900", "4100"],
            subTypeId: 18,
            nameContains: "Sales",
            cancellationToken);

    public static Task<int?> FindSalesTaxPayableAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        CancellationToken cancellationToken = default) =>
        FindAccountAsync(
            unitOfWork,
            companyId,
            ["25500", "2200"],
            subTypeId: 10,
            nameContains: "Sales Tax",
            cancellationToken);

    public static Task<int?> FindPurchasesOrInventoryAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        CancellationToken cancellationToken = default) =>
        FindAccountAsync(
            unitOfWork,
            companyId,
            ["12110", "50000", "5100", "1300"],
            subTypeId: 22,
            nameContains: "Cost of Goods Sold",
            cancellationToken);

    public static Task<int?> FindInputTaxRecoverableAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        CancellationToken cancellationToken = default) =>
        FindAccountAsync(
            unitOfWork,
            companyId,
            ["12910", "12810", "1400"],
            subTypeId: 6,
            nameContains: "Input Tax",
            cancellationToken);

    public static Task<int?> FindOpeningBalanceEquityAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        CancellationToken cancellationToken = default) =>
        FindAccountAsync(
            unitOfWork,
            companyId,
            ["30000", "32010", "3200"],
            subTypeId: 14,
            nameContains: "Opening Balance",
            cancellationToken);

    public static Task<int?> FindCashOrBankAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        CancellationToken cancellationToken = default) =>
        FindAccountAsync(
            unitOfWork,
            companyId,
            ["10015", "10800", "1100", "10000"],
            subTypeId: 1,
            nameContains: "Cash",
            cancellationToken);

    private static async Task<int?> FindAccountAsync(
        IUnitOfWork unitOfWork,
        int companyId,
        IReadOnlyList<string> preferredNumbers,
        int? subTypeId,
        string nameContains,
        CancellationToken cancellationToken)
    {
        var query = unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.IsActive);

        foreach (var number in preferredNumbers)
        {
            var byNumber = await query
                .Where(a => a.AccountNumber == number)
                .Select(a => (int?)a.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (byNumber.HasValue)
            {
                return byNumber;
            }
        }

        if (subTypeId.HasValue)
        {
            var bySubType = await query
                .Where(a => a.SubTypeId == subTypeId.Value)
                .OrderBy(a => a.AccountNumber)
                .Select(a => (int?)a.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (bySubType.HasValue)
            {
                return bySubType;
            }
        }

        return await query
            .Where(a => a.AccountName.Contains(nameContains))
            .OrderBy(a => a.AccountNumber)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
