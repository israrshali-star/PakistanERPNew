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
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IChartOfAccountsService, ChartOfAccountsService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerGlPostingService, CustomerGlPostingService>();
        services.AddScoped<IStackLotInventoryService, StackLotInventoryService>();
        services.AddScoped<ISalesInvoicePdfService, SalesInvoicePdfService>();
        services.AddScoped<ITradeInvoicePdfService, TradeInvoicePdfService>();
        services.AddScoped<IDeliveryChallanPdfService, DeliveryChallanPdfService>();
        services.AddScoped<ISalesInvoiceService, SalesInvoiceService>();
        services.AddScoped<IInvoiceShareService, InvoiceShareService>();
        services.AddScoped<ILedgerPdfService, LedgerPdfService>();
        services.AddScoped<ILedgerShareService, LedgerShareService>();
        services.AddScoped<ISalesInvoiceAttachmentService, SalesInvoiceAttachmentService>();
        services.AddScoped<IVendorBillAttachmentService, VendorBillAttachmentService>();
        services.AddScoped<ICustomerReceiptService, CustomerReceiptService>();
        services.AddScoped<ICustomerReceiptPdfService, CustomerReceiptPdfService>();
        services.AddScoped<ICustomerReceiptShareService, CustomerReceiptShareService>();
        services.AddScoped<IVendorPaymentPdfService, VendorPaymentPdfService>();
        services.AddScoped<IVendorPaymentShareService, VendorPaymentShareService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<IItemCartonSyncService, ItemCartonSyncService>();
        services.AddScoped<IItemCopyService, ItemCopyService>();
        services.AddScoped<IItemCategoryService, ItemCategoryService>();
        services.AddScoped<IUnitOfMeasureService, UnitOfMeasureService>();
        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<IFiscalYearService, FiscalYearService>();
        services.AddScoped<IInventoryTransactionService, InventoryTransactionService>();
        services.AddScoped<IInventoryReportService, InventoryReportService>();
        services.AddScoped<ISalesReportService, SalesReportService>();
        services.AddScoped<IPurchaseReportService, PurchaseReportService>();
        services.AddScoped<IDataExportService, DataExportService>();
        services.AddScoped<IFinancialReportService, FinancialReportService>();
        services.AddScoped<IJournalEntryService, JournalEntryService>();
        services.AddScoped<IVendorBillService, VendorBillService>();
        services.AddScoped<IVendorGlPostingService, VendorGlPostingService>();
        services.AddScoped<IVendorPaymentService, VendorPaymentService>();
        services.AddScoped<IBankGlPostingService, BankGlPostingService>();
        services.AddScoped<IBankService, BankService>();
        services.AddScoped<IBankTransactionService, BankTransactionService>();
        services.AddScoped<IBankReconciliationService, BankReconciliationService>();
        services.AddScoped<IVendorService, VendorService>();
        services.AddScoped<IQuickBooksIifImportService, QuickBooksIifImportService>();
        services.AddScoped<ICustomerExcelImportService, CustomerExcelImportService>();
        services.AddScoped<ICustomReportService, CustomReportService>();
        services.AddScoped<IGlRepairService, GlRepairService>();

        return services;
    }
}
