using PakistanAccountingERP.Application;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Infrastructure;
using PakistanAccountingERP.Infrastructure.Data.Seed;
using PakistanAccountingERP.Infrastructure.Services;
using PakistanAccountingERP.Web.Extensions;
using PakistanAccountingERP.Web.Middleware;
using Serilog;

if (TryRunCompanyPurge(args, out var purgeExitCode))
{
    Environment.Exit(purgeExitCode);
}

if (TryRunQuickBooksImport(args, out var importExitCode))
{
    Environment.Exit(importExitCode);
}

if (TryRunImportOpeningStock(args, out var openingStockExitCode))
{
    Environment.Exit(openingStockExitCode);
}

if (TryRunReapplyOpeningStockQuantityOnly(args, out var reapplyOpeningStockExitCode))
{
    Environment.Exit(reapplyOpeningStockExitCode);
}

if (TryRunCutoverReconcile(args, out var reconcileExitCode))
{
    Environment.Exit(reconcileExitCode);
}

if (TryRunTrialBalanceCoaImport(args, out var trialBalanceExitCode))
{
    Environment.Exit(trialBalanceExitCode);
}

if (TryRunBackdateOpeningJournals(args, out var backdateExitCode))
{
    Environment.Exit(backdateExitCode);
}

if (TryRunResyncOpeningBalances(args, out var resyncExitCode))
{
    Environment.Exit(resyncExitCode);
}

if (TryRunAlignTrialBalanceGl(args, out var alignExitCode))
{
    Environment.Exit(alignExitCode);
}

if (TryRunPostCutoverTransactions(args, out var postCutoverExitCode))
{
    Environment.Exit(postCutoverExitCode);
}

if (TryRunFixTrialBalanceMismatches(args, out var fixTrialBalanceExitCode))
{
    Environment.Exit(fixTrialBalanceExitCode);
}

if (TryRunChaseTrialBalanceGap(args, out var chaseTrialBalanceExitCode))
{
    Environment.Exit(chaseTrialBalanceExitCode);
}

if (TryRunRecalculateItemStock(args, out var recalculateItemStockExitCode))
{
    Environment.Exit(recalculateItemStockExitCode);
}

if (TryRunSyncItemCartons(args, out var syncItemCartonsExitCode))
{
    Environment.Exit(syncItemCartonsExitCode);
}

if (TryRunCopyItems(args, out var copyItemsExitCode))
{
    Environment.Exit(copyItemsExitCode);
}

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            path: Path.Combine("App_Data", "Logs", "pa-erp-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            shared: true));

    builder.Services.AddControllersWithViews()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSession(options =>
    {
        options.IdleTimeout = TimeSpan.FromMinutes(
            builder.Configuration.GetValue("AppSettings:SessionTimeoutMinutes", 60));
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
    });
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();
    builder.Services.AddWebServices();
    builder.Services.AddAppHealthChecks();
    builder.Services.AddHostedService<ScheduledBackupHostedService>();

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseSession();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<RequireCompanyMiddleware>();

    app.MapControllers();
    app.MapAppHealthChecks();
    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    await DbInitializer.InitializeAsync(app.Services);

    Log.Information("Pakistan Accounting ERP ready ({Environment})", app.Environment.EnvironmentName);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

static bool TryRunCompanyPurge(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 3
        || !string.Equals(args[0], "--purge-company-data", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var companyId))
    {
        return false;
    }

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var purger = scope.ServiceProvider.GetRequiredService<ICompanyDataPurgeService>();
        var result = purger.PurgeAsync(companyId).GetAwaiter().GetResult();
        Console.WriteLine(result.Message);
        Console.WriteLine($"Rows deleted: {result.RowsDeleted}");
        exitCode = result.Success ? 0 : 1;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunQuickBooksImport(string[] args, out int exitCode)
{
    exitCode = 0;

    var isIifImport = args.Length >= 4
        && string.Equals(args[0], "--import-iif", StringComparison.OrdinalIgnoreCase)
        && string.Equals(args[2], "--company-id", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(args[3], out _);

    var isReportImport = args.Length >= 3
        && string.Equals(args[0], "--import-qb-reports", StringComparison.OrdinalIgnoreCase)
        && string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(args[2], out _);

    if (!isIifImport && !isReportImport)
    {
        return false;
    }

    var companyId = isIifImport ? int.Parse(args[3]) : int.Parse(args[2]);
    var options = ParseQuickBooksImportOptions(args, isIifImport ? 4 : 3);
    var filePath = isIifImport ? Path.GetFullPath(args[1]) : string.Empty;

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var importer = scope.ServiceProvider.GetRequiredService<IQuickBooksIifImportService>();
        var result = isIifImport
            ? importer.ImportAsync(filePath, companyId, options).GetAwaiter().GetResult()
            : importer.ImportReportsAsync(companyId, options).GetAwaiter().GetResult();

        Console.WriteLine(result.Message);
        Console.WriteLine($"Accounts: {result.AccountsImported} imported, {result.AccountsSkipped} skipped");
        Console.WriteLine($"Items: {result.ItemsImported} imported, {result.ItemsSkipped} skipped");
        Console.WriteLine($"Customers: {result.CustomersImported} imported, {result.CustomersSkipped} skipped");
        Console.WriteLine($"Vendors: {result.VendorsImported} imported, {result.VendorsSkipped} skipped");
        Console.WriteLine($"Invoices: {result.InvoicesImported} imported, {result.InvoicesSkipped} skipped");
        Console.WriteLine($"Bills: {result.BillsImported} imported, {result.BillsSkipped} skipped");
        Console.WriteLine($"Item stock updated: {result.ItemsStockUpdated}, skipped: {result.ItemsStockSkipped}");
        Console.WriteLine($"Customer balances updated: {result.CustomerBalancesUpdated}");
        Console.WriteLine($"Vendor balances updated: {result.VendorBalancesUpdated}");

        if (isIifImport && result.Success && result.InvoicesImported == 0 && result.BillsImported == 0
            && result.CustomerBalancesUpdated == 0 && result.VendorBalancesUpdated == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Normal.IIF only contains master lists (accounts, items, customers, vendors).");
            Console.WriteLine("Export these QuickBooks reports to CSV, then re-run with:");
            Console.WriteLine("  --customer-balances-csv \"path\\Customer Balance Summary.csv\"");
            Console.WriteLine("  --vendor-balances-csv \"path\\Vendor Balance Summary.csv\"");
            Console.WriteLine("  --open-invoices-csv \"path\\Open Invoices.csv\"");
            Console.WriteLine("  --open-bills-csv \"path\\Unpaid Bills.csv\"");
        }

        exitCode = result.Success ? 0 : 1;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunImportOpeningStock(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 3
        || !string.Equals(args[0], "--import-opening-stock", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var companyId))
    {
        return false;
    }

    string? csvPath = null;
    var quantityOnly = false;

    for (var i = 3; i < args.Length; i++)
    {
        if (string.Equals(args[i], "--quantity-only", StringComparison.OrdinalIgnoreCase))
        {
            quantityOnly = true;
        }
        else if (!args[i].StartsWith('-') && csvPath is null)
        {
            csvPath = Path.GetFullPath(args[i]);
        }
    }

    if (string.IsNullOrWhiteSpace(csvPath))
    {
        Console.Error.WriteLine("--import-opening-stock requires a CSV file path.");
        exitCode = 1;
        return true;
    }

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var importer = scope.ServiceProvider.GetRequiredService<IQuickBooksIifImportService>();
        var options = new QuickBooksIifImportOptions
        {
            InventoryValuationCsvPath = csvPath,
            OpeningStockQuantityOnly = quantityOnly
        };
        var result = importer.ImportReportsAsync(companyId, options).GetAwaiter().GetResult();

        Console.WriteLine(result.Message);
        Console.WriteLine($"Item stock updated: {result.ItemsStockUpdated}, skipped: {result.ItemsStockSkipped}");
        Console.WriteLine($"Quantity-only mode: {quantityOnly}");
        exitCode = result.Success ? 0 : 1;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunReapplyOpeningStockQuantityOnly(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 3
        || !string.Equals(args[0], "--reapply-opening-stock-quantity-only", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var companyId))
    {
        return false;
    }

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var importer = scope.ServiceProvider.GetRequiredService<IQuickBooksIifImportService>();
        var result = importer.ReapplyOpeningStockQuantityOnlyAsync(companyId).GetAwaiter().GetResult();

        Console.WriteLine(result.Message);
        Console.WriteLine($"Bill lines zeroed: {result.BillLinesUpdated}");
        Console.WriteLine($"Inventory transactions zeroed: {result.TransactionsUpdated}");
        Console.WriteLine($"Items recalculated: {result.ItemsRecalculated}");
        exitCode = result.Success ? 0 : 1;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunCutoverReconcile(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 3
        || !string.Equals(args[0], "--reconcile-cutover", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var companyId))
    {
        return false;
    }

    var cutoverDate = new DateTime(2026, 6, 1);
    for (var i = 3; i < args.Length; i++)
    {
        if (string.Equals(args[i], "--cutover-date", StringComparison.OrdinalIgnoreCase)
            && i + 1 < args.Length
            && DateTime.TryParse(args[++i], out var parsed))
        {
            cutoverDate = parsed.Date;
        }
    }

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var repair = scope.ServiceProvider.GetRequiredService<IGlRepairService>();
        var result = repair.ReconcileToOpeningBalancesAsync(companyId, cutoverDate).GetAwaiter().GetResult();

        Console.WriteLine(result.Message);
        Console.WriteLine($"Transactional journals soft-deleted: {result.TransactionalJournalsSoftDeleted}");
        Console.WriteLine($"Sales invoices reverted to draft: {result.SalesInvoicesReverted}");
        Console.WriteLine($"Customer receipts removed: {result.CustomerReceiptsRemoved}");
        Console.WriteLine($"Bank transactions removed: {result.BankTransactionsRemoved}");
        Console.WriteLine($"Vendor bills reverted to draft: {result.VendorBillsReverted}");
        Console.WriteLine($"Sum customer opening balances: {result.SumCustomerOpeningBalance:N2}");
        Console.WriteLine($"Sum vendor opening balances: {result.SumVendorOpeningBalance:N2}");
        Console.WriteLine($"AR (11110) balance: {result.AccountsReceivableBalance:N2}");
        Console.WriteLine($"AP (20000) balance: {result.AccountsPayableBalance:N2}");

        exitCode = result.Success ? 0 : 1;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunResyncOpeningBalances(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 3
        || !string.Equals(args[0], "--resync-opening-balances", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var companyId))
    {
        return false;
    }

    var entryDate = new DateTime(2026, 5, 31);
    for (var i = 3; i < args.Length; i++)
    {
        if (string.Equals(args[i], "--date", StringComparison.OrdinalIgnoreCase)
            && i + 1 < args.Length
            && DateTime.TryParse(args[++i], out var parsed))
        {
            entryDate = parsed.Date;
        }
    }

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var repair = scope.ServiceProvider.GetRequiredService<IGlRepairService>();
        var count = repair.ResyncSubledgerOpeningBalancesAsync(companyId, entryDate).GetAwaiter().GetResult();
        Console.WriteLine($"Resynced {count} customer/vendor opening balance journals (entry date {entryDate:yyyy-MM-dd}).");
        exitCode = 0;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunAlignTrialBalanceGl(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 3
        || !string.Equals(args[0], "--align-trial-balance-gl", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var companyId))
    {
        return false;
    }

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var repair = scope.ServiceProvider.GetRequiredService<IGlRepairService>();
        var result = repair.AlignTrialBalanceGlAsync(companyId).GetAwaiter().GetResult();
        Console.WriteLine(result.Message);
        Console.WriteLine($"AR (11110): {result.AccountsReceivableBalance:N2}");
        Console.WriteLine($"AP (20000): {result.AccountsPayableBalance:N2}");
        exitCode = result.Success ? 0 : 1;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunTrialBalanceCoaImport(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 4
        || !string.Equals(args[0], "--import-trial-balance-coa", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var companyId))
    {
        return false;
    }

    var filePath = Path.GetFullPath(args[3]);

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var repair = scope.ServiceProvider.GetRequiredService<IGlRepairService>();
        var result = repair.ApplyTrialBalanceCoaOpeningsAsync(companyId, filePath).GetAwaiter().GetResult();

        Console.WriteLine(result.Message);
        Console.WriteLine($"Accounts updated: {result.AccountsUpdated}, skipped: {result.AccountsSkipped}");
        Console.WriteLine($"Banks synced: {result.BanksSynced}");
        Console.WriteLine($"AR (11110) balance: {result.AccountsReceivableBalance:N2}");
        Console.WriteLine($"AP (20000) balance: {result.AccountsPayableBalance:N2}");

        exitCode = result.Success ? 0 : 1;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunBackdateOpeningJournals(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 3
        || !string.Equals(args[0], "--backdate-opening-journals", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var companyId))
    {
        return false;
    }

    var entryDate = new DateTime(2026, 5, 31);
    for (var i = 3; i < args.Length; i++)
    {
        if (string.Equals(args[i], "--date", StringComparison.OrdinalIgnoreCase)
            && i + 1 < args.Length
            && DateTime.TryParse(args[++i], out var parsed))
        {
            entryDate = parsed.Date;
        }
    }

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var repair = scope.ServiceProvider.GetRequiredService<IGlRepairService>();
        var count = repair.BackdateOpeningBalanceJournalsAsync(companyId, entryDate).GetAwaiter().GetResult();
        Console.WriteLine($"Backdated {count} customer/vendor opening balance journals to {entryDate:yyyy-MM-dd}.");
        exitCode = 0;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunPostCutoverTransactions(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 3
        || !string.Equals(args[0], "--post-cutover-transactions", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var companyId))
    {
        return false;
    }

    var fromDate = new DateTime(2026, 6, 1);
    for (var i = 3; i < args.Length; i++)
    {
        if (string.Equals(args[i], "--from-date", StringComparison.OrdinalIgnoreCase)
            && i + 1 < args.Length
            && DateTime.TryParse(args[++i], out var parsed))
        {
            fromDate = parsed.Date;
        }
    }

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var repair = scope.ServiceProvider.GetRequiredService<IGlRepairService>();
        var result = repair.PostCutoverTransactionsAsync(companyId, fromDate).GetAwaiter().GetResult();

        Console.WriteLine(result.Message);
        Console.WriteLine($"Journals restored: {result.JournalsRestored}");
        Console.WriteLine($"Sales invoices posted: {result.SalesInvoicesPosted}");
        Console.WriteLine($"Vendor bills approved: {result.VendorBillsApproved}");
        Console.WriteLine($"Customer receipts restored: {result.CustomerReceiptsRestored}");
        Console.WriteLine($"Bank transactions restored: {result.BankTransactionsRestored}");
        Console.WriteLine($"Skipped duplicates: {result.SkippedDuplicates}");
        Console.WriteLine($"AR (11110): {result.AccountsReceivableBalance:N2}");
        Console.WriteLine($"AP (20000): {result.AccountsPayableBalance:N2}");
        Console.WriteLine($"Trial balance debits: {result.TrialBalanceDebits:N2}");
        Console.WriteLine($"Trial balance credits: {result.TrialBalanceCredits:N2}");

        exitCode = result.Success ? 0 : 1;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunFixTrialBalanceMismatches(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 3
        || !string.Equals(args[0], "--fix-trial-balance-mismatches", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var companyId))
    {
        return false;
    }

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var repair = scope.ServiceProvider.GetRequiredService<IGlRepairService>();
        var result = repair.FixTrialBalanceMismatchesAsync(companyId).GetAwaiter().GetResult();

        Console.WriteLine(result.Message);
        Console.WriteLine($"Customer receipt journals fixed: {result.CustomerReceiptJournalsFixed}");
        Console.WriteLine($"Duplicate vendor bills reversed: {result.DuplicateVendorBillsReversed}");
        Console.WriteLine($"Kept Aside opening set: {result.KeptAsideOpeningSet}");
        Console.WriteLine($"Cash (10015): {result.CashBalance:N2}");
        Console.WriteLine($"AR (11110): {result.AccountsReceivableBalance:N2}");
        Console.WriteLine($"Inventory (12110): {result.InventoryBalance:N2}");
        Console.WriteLine($"AP (20000): {result.AccountsPayableBalance:N2}");
        Console.WriteLine($"Kept Aside (10016): {result.KeptAsideBalance:N2}");
        Console.WriteLine($"Trial balance debits: {result.TrialBalanceDebits:N2}");
        Console.WriteLine($"Trial balance credits: {result.TrialBalanceCredits:N2}");

        exitCode = result.Success ? 0 : 1;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunChaseTrialBalanceGap(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 3
        || !string.Equals(args[0], "--chase-trial-balance-gap", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var companyId))
    {
        return false;
    }

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var repair = scope.ServiceProvider.GetRequiredService<IGlRepairService>();
        var result = repair.ChaseTrialBalanceGapAsync(companyId).GetAwaiter().GetResult();

        Console.WriteLine(result.Message);
        Console.WriteLine($"Sales tax payments reclassified: {result.SalesTaxPaymentsReclassified}");
        Console.WriteLine($"Customer bank transactions reposted: {result.BankTransactionsReposted}");
        Console.WriteLine($"AR (11110): {result.AccountsReceivableBalance:N2}");
        Console.WriteLine($"AP (20000): {result.AccountsPayableBalance:N2}");
        Console.WriteLine($"Sales tax (25500): {result.SalesTaxPayableBalance:N2}");
        Console.WriteLine($"ERP trial balance debits: {result.TrialBalanceDebits:N2}");
        Console.WriteLine($"ERP trial balance credits: {result.TrialBalanceCredits:N2}");
        Console.WriteLine($"QB trial balance total: {result.QuickBooksTotalDebits:N2}");
        Console.WriteLine($"Remaining gap vs QB: {result.RemainingGapDebits:N2}");

        exitCode = result.Success ? 0 : 1;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunSyncItemCartons(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 3
        || !string.Equals(args[0], "--sync-item-cartons", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var companyId))
    {
        return false;
    }

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var sync = scope.ServiceProvider.GetRequiredService<IItemCartonSyncService>();
        sync.SyncCompanyItemsAsync(companyId).GetAwaiter().GetResult();
        Console.WriteLine($"Item cartons synced for company {companyId}.");
        exitCode = 0;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunCopyItems(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 5
        || !string.Equals(args[0], "--copy-items", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--from-company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var sourceCompanyId)
        || !string.Equals(args[3], "--to-company-ids", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var targetCompanyIds = args[4]
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(id => int.TryParse(id, out var companyId) ? companyId : (int?)null)
        .Where(id => id.HasValue)
        .Select(id => id!.Value)
        .ToList();

    if (targetCompanyIds.Count == 0)
    {
        return false;
    }

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var copy = scope.ServiceProvider.GetRequiredService<IItemCopyService>();
        var result = copy.CopyItemsAsync(sourceCompanyId, targetCompanyIds).GetAwaiter().GetResult();

        Console.WriteLine(result.Message);
        Console.WriteLine($"Categories created: {result.CategoriesCreated}");
        Console.WriteLine($"Items created: {result.ItemsCreated}");
        Console.WriteLine($"Items skipped (already exist): {result.ItemsSkipped}");

        exitCode = result.Success ? 0 : 1;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static bool TryRunRecalculateItemStock(string[] args, out int exitCode)
{
    exitCode = 0;

    if (args.Length < 3
        || !string.Equals(args[0], "--recalculate-item-stock", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(args[2], out var companyId))
    {
        return false;
    }

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var repair = scope.ServiceProvider.GetRequiredService<IGlRepairService>();
        var result = repair.RecalculateItemStockAsync(companyId).GetAwaiter().GetResult();

        Console.WriteLine(result.Message);
        Console.WriteLine($"Items updated: {result.ItemsUpdated}");
        Console.WriteLine($"Sum item CurrentStock: {result.SumItemStock:N2}");
        Console.WriteLine($"Sum inventory transactions: {result.SumTransactionStock:N2}");

        exitCode = result.Success ? 0 : 1;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static QuickBooksIifImportOptions ParseQuickBooksImportOptions(string[] args, int startIndex)
{
    var options = new QuickBooksIifImportOptions();

    for (var i = startIndex; i < args.Length; i++)
    {
        switch (args[i].ToLowerInvariant())
        {
            case "--customer-balances-csv" when i + 1 < args.Length:
                options.CustomerBalancesCsvPath = Path.GetFullPath(args[++i]);
                break;
            case "--vendor-balances-csv" when i + 1 < args.Length:
                options.VendorBalancesCsvPath = Path.GetFullPath(args[++i]);
                break;
            case "--open-invoices-csv" when i + 1 < args.Length:
                options.OpenInvoicesCsvPath = Path.GetFullPath(args[++i]);
                break;
            case "--open-bills-csv" when i + 1 < args.Length:
                options.OpenBillsCsvPath = Path.GetFullPath(args[++i]);
                break;
            case "--inventory-valuation-csv" when i + 1 < args.Length:
                options.InventoryValuationCsvPath = Path.GetFullPath(args[++i]);
                break;
            case "--opening-stock-quantity-only":
                options.OpeningStockQuantityOnly = true;
                break;
            case "--skip-master-data":
                options.SkipMasterData = true;
                break;
            case "--cutover-date" when i + 1 < args.Length
                && DateTime.TryParse(args[++i], out var cutoverDate):
                options.CutoverDate = cutoverDate.Date;
                break;
        }
    }

    return options;
}
