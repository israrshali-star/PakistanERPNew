using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Infrastructure.Data;

namespace PakistanAccountingERP.Infrastructure.Services;

public class CompanyDataPurgeService : ICompanyDataPurgeService
{
    private readonly AppDbContext _context;
    private readonly ILogger<CompanyDataPurgeService> _logger;

    public CompanyDataPurgeService(AppDbContext context, ILogger<CompanyDataPurgeService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CompanyDataPurgeResult> PurgeAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var companyExists = await _context.Companies
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return new CompanyDataPurgeResult(false, $"Company id {companyId} was not found.", 0);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var totalDeleted = 0;
            totalDeleted += await ExecuteDeleteAsync(
                """
                DELETE FROM SalesInvoiceAttachments WHERE CompanyId = {0};
                DELETE FROM SalesInvoiceLines WHERE SalesInvoiceId IN (SELECT Id FROM SalesInvoices WHERE CompanyId = {0});
                DELETE FROM SalesInvoices WHERE CompanyId = {0};
                DELETE FROM VendorBillAttachments WHERE CompanyId = {0};
                DELETE FROM VendorBillLines WHERE VendorBillId IN (SELECT Id FROM VendorBills WHERE CompanyId = {0});
                DELETE FROM VendorBills WHERE CompanyId = {0};
                DELETE FROM CustomerReceipts WHERE CompanyId = {0};
                DELETE FROM VendorPayments WHERE CompanyId = {0};
                DELETE FROM JournalEntryLines WHERE JournalEntryId IN (SELECT Id FROM JournalEntries WHERE CompanyId = {0});
                DELETE FROM JournalEntries WHERE CompanyId = {0};
                DELETE FROM BankReconciliations WHERE CompanyId = {0};
                DELETE FROM BankTransactions WHERE CompanyId = {0};
                DELETE FROM Banks WHERE CompanyId = {0};
                DELETE FROM InventoryTransactions WHERE CompanyId = {0};
                DELETE FROM Items WHERE CompanyId = {0};
                DELETE FROM ItemCategories WHERE CompanyId = {0};
                DELETE FROM Customers WHERE CompanyId = {0};
                DELETE FROM Vendors WHERE CompanyId = {0};
                DELETE FROM Warehouses WHERE CompanyId = {0};
                DELETE FROM FiscalYears WHERE CompanyId = {0};
                UPDATE ChartOfAccounts SET ParentAccountId = NULL WHERE CompanyId = {0};
                DELETE FROM ChartOfAccounts WHERE CompanyId = {0};
                DELETE FROM TaxSettings WHERE CompanyId = {0};
                DELETE FROM AuditLogs WHERE CompanyId = {0};
                DELETE FROM DataExportHistories WHERE CompanyId = {0};
                """,
                companyId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Purged {RowsDeleted} rows of business data for company {CompanyId}",
                totalDeleted,
                companyId);

            return new CompanyDataPurgeResult(
                true,
                $"Purged all business data for company {companyId}. Company record and user access were kept.",
                totalDeleted);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to purge company {CompanyId}", companyId);
            return new CompanyDataPurgeResult(false, $"Purge failed: {ex.Message}", 0);
        }
    }

    private async Task<int> ExecuteDeleteAsync(
        string sql,
        int companyId,
        CancellationToken cancellationToken)
    {
        return await _context.Database.ExecuteSqlRawAsync(sql, [companyId], cancellationToken);
    }
}
