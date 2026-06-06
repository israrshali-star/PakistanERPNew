using Microsoft.Extensions.DependencyInjection;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Application.Services;

namespace PakistanAccountingERP.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<ICompanySettingsService, CompanySettingsService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IChartOfAccountsService, ChartOfAccountsService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ISalesInvoiceService, SalesInvoiceService>();
        services.AddScoped<ICustomerReceiptService, CustomerReceiptService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<IItemCategoryService, ItemCategoryService>();
        services.AddScoped<IUnitOfMeasureService, UnitOfMeasureService>();
        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<IInventoryTransactionService, InventoryTransactionService>();
        services.AddScoped<IInventoryReportService, InventoryReportService>();
        services.AddScoped<ISalesReportService, SalesReportService>();
        services.AddScoped<IPurchaseReportService, PurchaseReportService>();
        services.AddScoped<IFinancialReportService, FinancialReportService>();
        services.AddScoped<IJournalEntryService, JournalEntryService>();
        services.AddScoped<IVendorBillService, VendorBillService>();
        services.AddScoped<IVendorPaymentService, VendorPaymentService>();
        services.AddScoped<IBankService, BankService>();
        services.AddScoped<IBankTransactionService, BankTransactionService>();
        services.AddScoped<IBankReconciliationService, BankReconciliationService>();
        services.AddScoped<IVendorService, VendorService>();

        return services;
    }
}
